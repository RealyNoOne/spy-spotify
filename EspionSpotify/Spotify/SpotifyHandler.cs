﻿using EspionSpotify.AudioSessions;
using EspionSpotify.Events;
using EspionSpotify.Models;
using System;
using System.Threading.Tasks;
using System.Timers;

namespace EspionSpotify.Spotify
{
    public class SpotifyHandler: ISpotifyHandler, IDisposable
    {
        public const int EVENT_TIMER_INTERVAL = 50;
        public const int SONG_TIMER_INTERVAL = 1000;

        public Timer EventTimer { get; private set; }
        public Timer SongTimer { get; private set; }
        public ISpotifyProcess SpotifyProcess { get; private set; }
        public ISpotifyStatus SpotifyLatestStatus { get; private set; }

        private bool _listenForEvents;
        public bool ListenForEvents
        {
            get
            {
                return _listenForEvents;
            }
            set
            {
                _listenForEvents = value;
                EventTimer.Enabled = value;
            }
        }

        public Track Track { get; set; }

        public SpotifyHandler(IMainAudioSession audioSession): this(
            spotifyProcess: new SpotifyProcess(audioSession)
        ) {}

        public SpotifyHandler(ISpotifyProcess spotifyProcess)
        {
            SpotifyProcess = spotifyProcess;
            EventTimer = new Timer();
            SongTimer = new Timer();
            AttachTimerToTickEvent();
        }

        public event EventHandler<TrackChangeEventArgs> OnTrackChange;

        public event EventHandler<PlayStateEventArgs> OnPlayStateChange;

        public event EventHandler<TrackTimeChangeEventArgs> OnTrackTimeChange;

        public async Task<Track> GetTrack()
        {
            if (SpotifyLatestStatus == null)
            {
                return (await SpotifyProcess.GetSpotifyStatus())?.CurrentTrack;
            }

            return SpotifyLatestStatus.GetTrack();
        }

        public async void ElapsedEventTick(object sender, ElapsedEventArgs e)
        {
            SpotifyLatestStatus = await SpotifyProcess.GetSpotifyStatus();
            if (SpotifyLatestStatus?.CurrentTrack == null)
            {
                EventTimer.Start();
                return;
            }

            var newestTrack = SpotifyLatestStatus.CurrentTrack;
            if (Track != null)
            {
                if (newestTrack.Playing != Track.Playing)
                {
                    if (newestTrack.Playing)
                    {
                        SongTimer.Start();
                    }
                    else
                    {
                        SongTimer.Stop();
                    }

                    _ = Task.Run(() => OnPlayStateChange?.Invoke(this, new PlayStateEventArgs()
                    {
                        Playing = newestTrack.Playing
                    }));
                }
                if (!newestTrack.Equals(Track))
                {
                    SongTimer.Start();
                    _ = Task.Run(() => OnTrackChange?.Invoke(this, new TrackChangeEventArgs()
                    {
                        OldTrack = Track,
                        NewTrack = SpotifyLatestStatus.GetTrack()
                    }));
                }
                if (Track.CurrentPosition != null || newestTrack != null)
                {
                    _ = Task.Run(() => OnTrackTimeChange?.Invoke(this, new TrackTimeChangeEventArgs()
                    {
                        TrackTime = newestTrack.Equals(Track) ? Track?.CurrentPosition ?? 0 : 0
                    }));
                }
            }
            if (newestTrack != null)
            {
                newestTrack.CurrentPosition = newestTrack.Equals(Track) ? Track?.CurrentPosition ?? 0 : (int?)null;
                Track = newestTrack;
            }
            EventTimer.Start();
        }

        private void ElapsedSongTick(object sender, ElapsedEventArgs e)
        {
            if (Track == null) return;

            Track.CurrentPosition++;
        }

        private void AttachTimerToTickEvent()
        {
            EventTimer.Interval = EVENT_TIMER_INTERVAL;
            EventTimer.AutoReset = false;
            EventTimer.Enabled = false;
            EventTimer.Elapsed += ElapsedEventTick;

            SongTimer.Interval = SONG_TIMER_INTERVAL;
            SongTimer.AutoReset = true;
            SongTimer.Enabled = false;
            SongTimer.Elapsed += ElapsedSongTick;
        }

        public void Dispose()
        {
            if (EventTimer != null)
            {
                EventTimer.Enabled = false;
                EventTimer.Elapsed -= ElapsedEventTick;
                EventTimer.Dispose();
            }

            if (SongTimer != null)
            {
                SongTimer.Enabled = false;
                SongTimer.Elapsed -= ElapsedSongTick;
                SongTimer.Dispose();
            }
        }
    }
}
