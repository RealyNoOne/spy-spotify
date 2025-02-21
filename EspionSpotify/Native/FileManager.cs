﻿using EspionSpotify.Extensions;
using EspionSpotify.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.RegularExpressions;

namespace EspionSpotify.Native
{
    public class FileManager
    {
        public const int MAX_PATH_LENGTH = 260;
        public const int MIN_PATH_LEFT_LENGTH = 100;

        private const int FILE_COUNTER_AND_EXTENSION_LENGTH = 10;

        private readonly UserSettings _userSettings;
        private readonly Track _track;
        private readonly IFileSystem _fileSystem;
        private readonly DateTime _now;

        internal FileManager(UserSettings userSettings, Track track, IFileSystem fileSystem) :
            this(userSettings, track, fileSystem, now: DateTime.Now)
        { }

        public FileManager(UserSettings userSettings, Track track, IFileSystem fileSystem, DateTime now)
        {
            _userSettings = userSettings;
            _track = track;
            _fileSystem = fileSystem;
            _now = now;
        }

        internal string GetTempFile()
        {
            var tempFile = _fileSystem.Path.GetTempFileName();
            var path = _fileSystem.Path.GetDirectoryName(tempFile);
            var tempFileName = _fileSystem.Path.GetFileNameWithoutExtension(tempFile);
            var extension = _fileSystem.Path.GetExtension(tempFile);

            var fileName = string.Join(".", new string[] { tempFileName, Constants.SPYTIFY.ToLower() });

            return ConcatPaths(path, $"{fileName}{extension}");
        }

        internal static bool GoesAtRoot(bool GroupByFoldersEnabled, bool IsUnknown) => !GroupByFoldersEnabled || IsUnknown;

        public OutputFile GetOutputFile()
        {
            var (artistDirectory, albumDirectory) = GetFolderPath(_track, _userSettings);
            CreateDirectories(_userSettings, artistDirectory, albumDirectory);

            var (fileMaxLength, _) = GetFileMaxLength(_track, _userSettings);

            var outputFile = new OutputFile
            {
                BasePath = _userSettings.OutputPath,
                FoldersPath = ConcatPaths(artistDirectory, albumDirectory),
                MediaFile = GenerateFileName(_track, _userSettings, _now).ToMaxLength(fileMaxLength),
                Separator = _userSettings.TrackTitleSeparator,
                Extension = GetMediaFormatExtension(_userSettings),
            };
            
            switch (_userSettings.RecordRecordingsStatus)
            {
                case Enums.RecordRecordingsStatus.Duplicate:
                    while (_fileSystem.File.Exists(outputFile.ToMediaFilePath()))
                    {
                        outputFile.Increment();
                    }
                    return outputFile;
                default:
                    return outputFile;
            }
        }

        public void DeleteFile(string currentFile)
        {
            if (string.IsNullOrWhiteSpace(currentFile))
            {
                throw new Exception($"File name cannot be null.");
            }
            try
            {
                if (_fileSystem.File.Exists(currentFile))
                {
                    _fileSystem.File.Delete(currentFile);
                }

                if (!GoesAtRoot(_userSettings.GroupByFoldersEnabled, _track.IsUnknown) && Path.GetExtension(currentFile).ToLowerInvariant() != ".tmp")
                {
                    DeleteFileFolder(currentFile);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        public void RenameFile(string source, string destination)
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(destination))
            {
                throw new Exception($"Source / Destination file name cannot be null.");
            }

            if (_fileSystem.File.Exists(source))
            {
                try
                {
                    if (_fileSystem.File.Exists(destination))
                    {
                        _fileSystem.File.Delete(destination);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }

                _fileSystem.File.Move(source, destination);
            }
        }

        public bool IsPathFileNameExists(Track track, UserSettings userSettings, IFileSystem fileSystem)
        {
            var (artistDirectory, albumDirectory) = GetFolderPath(track, userSettings);
            var pathWithFolder = ConcatPaths(userSettings.OutputPath, ConcatPaths(artistDirectory, albumDirectory));
            var fileName = GenerateFileName(track, userSettings, _now);
            var filePath = ConcatPaths(pathWithFolder, $"{fileName}.{GetMediaFormatExtension(userSettings)}");
            return fileSystem.File.Exists(filePath);
        }

        public static (string, string) GetFolderPath(Track track, UserSettings userSettings)
        {
            if (GoesAtRoot(userSettings.GroupByFoldersEnabled, track.IsUnknown)) return (null, null);

            var (maxLength, _) = GetFolderMaxLength(userSettings);

            var artistDir = GetArtistDirectoryName(track, userSettings.TrackTitleSeparator, maxLength);
            var albumDir = GetAlbumDirectoryName(track, userSettings.TrackTitleSeparator, maxLength);

            if (string.IsNullOrEmpty(artistDir) || string.IsNullOrEmpty(albumDir)) throw new Exception("Artist / Album cannot be null.");

            return (artistDir, albumDir);
        }

        public static string GetCleanPath(string path)
        {
            return Regex.Replace(path.TrimEndPath(), $"[{Regex.Escape(new string(Path.GetInvalidPathChars()))}]", string.Empty);
        }

        public static string GetCleanFileFolder(string name, int maxLength)
        {
            return Regex.Replace(name.TrimEndPath(), $"[{Regex.Escape(new string(Path.GetInvalidFileNameChars()))}]", string.Empty).ToMaxLength(maxLength);
        }

        public static string ConcatPaths(params string[] paths)
        {
            return string.Join(@"\", paths.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        public static string GetArtistDirectoryName(Track track, string trackTitleSeparator = " ", int maxLength = -1)
        {
            var artistDir = Normalize.RemoveDiacritics(track.Artists);
            return GetCleanFileFolder(artistDir, maxLength: maxLength).Replace(" ", trackTitleSeparator);
        }

        public static string GetAlbumDirectoryName(Track track, string trackTitleSeparator = " ", int maxLength = -1)
        {
            var albumInfos = new List<string>() { string.IsNullOrEmpty(track.Album) ? Constants.UNTITLED_ALBUM : Normalize.RemoveDiacritics(track.Album) };
            if (track.Year.HasValue) albumInfos.Add($"({track.Year.Value})");
            return GetCleanFileFolder(string.Join(" ", albumInfos), maxLength: maxLength).Replace(" ", trackTitleSeparator);
        }

        public static (int, string) GetFolderMaxLength(UserSettings userSettings)
        {
            if (userSettings.GroupByFoldersEnabled)
            {
                const int NUMBER_OF_SUB_PATHS_TO_CREATE = 3; // contains artist, album, title
                var pathShape = string.Join(@"\", new[] { userSettings.OutputPath }.Concat(new string[NUMBER_OF_SUB_PATHS_TO_CREATE]));
                var maxLengthPerFolder = (MAX_PATH_LENGTH - pathShape.Length) / NUMBER_OF_SUB_PATHS_TO_CREATE;
                return (maxLengthPerFolder, pathShape);
            }
            else
            {

                const int NUMBER_OF_SUB_PATHS_TO_CREATE = 1; // contains title
                var pathShape = string.Join(@"\", new[] { userSettings.OutputPath }.Concat(new string[NUMBER_OF_SUB_PATHS_TO_CREATE]));
                var maxLengthPerFolder = (MAX_PATH_LENGTH - pathShape.Length) / 1;
                return (maxLengthPerFolder, pathShape);
            }
        }

        public static (int, string) GetFileMaxLength(Track track, UserSettings userSettings)
        {
            var pathShape = string.Join(@"\", userSettings.OutputPath, null);

            if (userSettings.GroupByFoldersEnabled)
            {
                var artistDirectory = GetArtistDirectoryName(track, userSettings.TrackTitleSeparator, maxLength: -1);
                var albumDirectory = GetAlbumDirectoryName(track, userSettings.TrackTitleSeparator, maxLength: -1);
                pathShape = string.Join(@"\", userSettings.OutputPath, artistDirectory, albumDirectory, null);
            }

            var maxLength = Math.Max(MAX_PATH_LENGTH - pathShape.Length - FILE_COUNTER_AND_EXTENSION_LENGTH, 0);

            return (maxLength, pathShape);
        }

        public static bool IsOutputPathTooLong(string path)
        {
            return (MAX_PATH_LENGTH - path.Length) <= MIN_PATH_LEFT_LENGTH;
        }

        private void CreateDirectories(UserSettings userSettings, string artistDirectory, string albumDirectory)
        {
            if (!userSettings.GroupByFoldersEnabled) return;
            if (string.IsNullOrEmpty(artistDirectory) || string.IsNullOrEmpty(albumDirectory)) return;

            CreateDirectory(userSettings.OutputPath, artistDirectory);
            CreateDirectory(userSettings.OutputPath, artistDirectory, albumDirectory);
        }

        private void CreateDirectory(string outputPath, params string[] directories)
        {
            if (directories.Any(x => string.IsNullOrWhiteSpace(x)))
            {
                throw new Exception("One or more directories has no name.");
            }

            var path = ConcatPaths(new[] { outputPath }.Concat(directories).ToArray());
            if (_fileSystem.Directory.Exists(path)) return;
            _fileSystem.Directory.CreateDirectory(path);
        }

        private static string GetMediaFormatExtension(UserSettings userSettings)
        {
            return userSettings.MediaFormat.ToString().ToLower();
        }

        private static string GenerateFileName(Track track, UserSettings userSettings, DateTime now)
        {
            if (string.IsNullOrEmpty(track.Artist)) throw new Exception("Cannot recognize this type of track.");
            
            var fileName = Normalize.RemoveDiacritics(
                GoesAtRoot(userSettings.GroupByFoldersEnabled, track.IsUnknown)
                    ? track.ToString()
                    : track.ToTitleString());

            if (track.Ad && !track.IsUnknownPlaying)
            {
                fileName = $"{Constants.ADVERTISEMENT} {now.ToString("yyyyMMddHHmmss")}";
            }
            
            fileName = GetCleanFileFolder(fileName, maxLength: FileManager.MAX_PATH_LENGTH);

            if (string.IsNullOrWhiteSpace(fileName)) throw new Exception("File name cannot be empty.");
            if (new[] { Constants.SPOTIFY, Constants.SPOTIFYFREE }.Contains(fileName)) throw new Exception($"File name cannot be {Constants.SPOTIFY}.");

            var trackNumber = userSettings.OrderNumberInfrontOfFileEnabled ? (userSettings.OrderNumberAsFile ?? 0).ToString($"{userSettings.OrderNumberMask} ") : null;
            return Regex.Replace($"{trackNumber}{fileName}", @"\s", userSettings.TrackTitleSeparator ?? " "); ;
        }

        private void DeleteFileFolder(string currentFile)
        {
            var folderPath = _fileSystem.Path.GetDirectoryName(currentFile);
            if (!_fileSystem.Directory.EnumerateFiles(folderPath).Any())
            {
                try { _fileSystem.Directory.Delete(folderPath); }
                catch (Exception ex) { Console.WriteLine(ex.Message); }

                var subFolderPath = _fileSystem.Path.GetDirectoryName(folderPath);
                if (_userSettings.OutputPath != subFolderPath && subFolderPath.Contains(_userSettings.OutputPath))
                {
                    DeleteFileFolder(folderPath);
                }
            }
        }
    }
}
