using System;
using System.Collections.Generic;
using System.IO;
using NAudio.Wave;

namespace AbevBot
{
  public class Notification
  {
    private static readonly TimeSpan MinimumNotificationTime = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MaximumNotificationTime = new(0, 1, 0);

    public bool Started { get; private set; }
    private DateTime StartTime { get; set; }
    public string TextToDisplay { get; init; }
    public string TextToRead { get; init; }
    public float TTSVolume { get; init; } = 1f;
    public string SoundPath { get; init; }
    public float SoundVolume { get; init; } = 1f;
    public string VideoPath { get; init; }
    public float VideoVolume { get; init; } = 1f;
    private bool VideoEnded;
    private bool VideoStarted;
    private bool TextDisplayed;
    private bool SoundPlayed;
    private bool TTSPlayed;
    private readonly List<WaveOut> AudioToPlay = new();
    private bool AudioStarted;

    /// <summary> Initializes required things and starts the notification </summary>
    public void Start()
    {
      if (Started) return;
      Started = true;

      VideoEnded = VideoPath is null || VideoPath.Length == 0;
      SoundPlayed = SoundPath is null || SoundPath.Length == 0;
      TTSPlayed = TextToRead is null || TextToRead.Length == 0;

      if (!TTSPlayed)
      {
        // There is TTS to play, find all of the voices in the message and split the message to be read by different voices
        var sampleSounds = Notifications.GetSampleSounds();
        List<ISampleProvider> sounds = new();
        List<string> text = new();
        int index;
        text.AddRange(TextToRead.Split(':'));

        if (text.Count == 0) { MainWindow.ConsoleWarning($">> Nothing to read: {TextToRead}"); } // Nothing to read? Do nothing
        else if (text.Count == 1) { NoIdeaForTheName(text[^1], ref sampleSounds, ref sounds, "StreamElements", "Brian"); } // Just text, read with default voice
        else
        {
          // There is at least one attempt to change the voice
          string voice, maybeVoice;
          while (text.Count > 1)
          {
            // Find a space before voice name
            index = text[^2].LastIndexOf(" ");
            if (index <= 0) { maybeVoice = text[^2].Trim(); } // Whole text[^2] is voice name, try to get it
            else { maybeVoice = text[^2].Substring(index).Trim(); } // Part of text[^2] is voice name - extract it

            voice = StreamElements.GetVoice(maybeVoice);
            if (voice?.Length > 0)
            {
              NoIdeaForTheName(text[^1].Trim(), ref sampleSounds, ref sounds, "StreamElements", voice);
              text.RemoveAt(text.Count - 1);
              if (index <= 0) { text.RemoveAt(text.Count - 1); }
              else { text[^1] = text[^1].Substring(0, index); }
              continue;
            }
            voice = TikTok.GetVoice(maybeVoice);
            if (voice?.Length > 0)
            {
              NoIdeaForTheName(text[^1].Trim(), ref sampleSounds, ref sounds, "TikTok", voice);
              text.RemoveAt(text.Count - 1);
              if (index <= 0) { text.RemoveAt(text.Count - 1); }
              else { text[^1] = text[^1].Substring(0, index); }
              continue;
            }

            // The voice is not found, join [^2] with [^1] and ':' symbol, and remove last text that was merged
            text[^2] = string.Join(':', text[^2], text[^1]);
            text.RemoveAt(text.Count - 1);
          }

          if (text.Count == 1)
          {
            // A text to read is left, so no voice is found for it, read it with default voice
            // It may be that there was a voice change after a voice change - try to find the voice with remaining text
            if (text[0].Trim().Length > 0)
            {
              maybeVoice = text[0].Trim();
              voice = StreamElements.GetVoice(maybeVoice);
              if (voice?.Length == 0) { voice = TikTok.GetVoice(maybeVoice); }
              if (voice is null || voice?.Length == 0) { NoIdeaForTheName(text[0].Trim(), ref sampleSounds, ref sounds, "StreamElements", "Brian"); }
              else { } // The remaining part was also a voice change so, do nothing? May add null to sounds but what's the point of it?
            }
          }
          else if (text.Count != 0)
          {
            // Something bad happened
            MainWindow.ConsoleWarning(">> Something bad happened with TTS generation");
          }
        }

        AudioToPlay.Insert(0, Audio.GetWavSound(sounds, TTSVolume));
      }
      if (!SoundPlayed)
      {
        AudioToPlay.Insert(0, Audio.GetWavSound(SoundPath, SoundVolume));
      }

      StartTime = DateTime.Now;
    }

    /// <summary> Update status of playing notification. </summary>
    /// <returns> <value>true</value> if notification ended. </returns>
    public bool Update()
    {
      if (!Started) return false;

      if (DateTime.Now - StartTime > MaximumNotificationTime)
      {
        MainWindow.ConsoleWarning(">> Maximum notification time reached, something went wrong, to not block other notificaitons force closing this one!");
        MainWindow.SetTextDisplayed(string.Empty);
        MainWindow.StopVideoPlayer();
        AudioToPlay[0]?.Stop();
        AudioToPlay.Clear();
        return true;
      }

      if (!VideoEnded)
      {
        if (!VideoStarted && !Notifications.SkipNotification)
        {
          // Start the video
          VideoStarted = true;
          MainWindow.StartVideoPlayer(VideoPath, VideoVolume);
        }
        else if (Notifications.SkipNotification)
        {
          MainWindow.StopVideoPlayer();
          VideoEnded = true;
        }
        else if (MainWindow.VideoEnded) { VideoEnded = true; }
        if (!VideoEnded) return false;
      }

      // Display text
      if (!TextDisplayed && !Notifications.NotificationsPaused && !Notifications.SkipNotification && TextToDisplay?.Length > 0)
      {
        TextDisplayed = true;
        MainWindow.SetTextDisplayed(TextToDisplay);
      }

      // Play the audio
      if (AudioToPlay.Count > 0)
      {
        if (Notifications.SkipNotification)
        {
          // Skip notification active - stop current audio and clear the queue
          AudioToPlay[0]?.Stop();
          AudioToPlay.Clear();
        }
        else if (AudioToPlay[0] is null)
        {
          // For some reason the GetXXXSound returned null - skip the sound
          MainWindow.ConsoleWarning("> null WaveOut reference in AudioToPlay list!");
          AudioToPlay.RemoveAt(0);
        }
        else if (Notifications.NotificationsPaused && AudioToPlay[0]?.PlaybackState == PlaybackState.Playing)
        {
          // Pause notification active and the sound is playing - pause it
          AudioToPlay[0].Pause();
        }
        else if (!Notifications.NotificationsPaused && AudioToPlay[0]?.PlaybackState == PlaybackState.Paused)
        {
          // Pause notification not active and the sound is not playing - play it
          AudioToPlay[0].Play();
        }
        else if (AudioToPlay[0].PlaybackState == PlaybackState.Stopped)
        {
          // Audio is stopped, it wasn't started yet or it finished playing
          if (!AudioStarted)
          {
            AudioToPlay[0].Play();
            AudioStarted = true;
          }
          else
          {
            AudioToPlay.RemoveAt(0);
            AudioStarted = false;
          }
        }

        if (AudioToPlay.Count > 0) return false;
      }

      // The notification is over, clear after it
      if (!Notifications.SkipNotification && (DateTime.Now - StartTime < MinimumNotificationTime)) return false;
      MainWindow.SetTextDisplayed(string.Empty); // Clear displayed text

      return true; // return true when notification has ended
    }

    // FIXME: Figure out good method name :D
    // It searches for sound samples in provided text,
    // splits the text to parts that have to be read, and parts that should play sound sample
    // It also inserts new sounds at the beginning of provided sounds array
    // GettoTTSoAndoInsertoSamplesoAndoMergoWithoProvidedoSoundeso??
    private void NoIdeaForTheName(string _text, ref Dictionary<string, FileInfo> sampleSounds, ref List<ISampleProvider> sounds, string supplier, string voice)
    {
      string text = _text;
      string maybeSample;
      int index, nextIndex;
      List<ISampleProvider> newAudio = new();

      while (text.Length > 0)
      {
        index = text.IndexOf('-');
        if (index >= 0)
        {
          nextIndex = text.IndexOf(" ", index); // Space index (end of word after '-' symbol)
          if (nextIndex > index || nextIndex == -1)
          {
            // We got indexes between a word that starts with '-' symbol is placed (-1 means to the end of the text)
            maybeSample = text.Substring(index + 1, nextIndex > 0 ? (nextIndex - index - 1) : (text.Length - index - 1));
            // Check if the sample exist in samples list
            if (sampleSounds.ContainsKey(maybeSample.Trim().ToLower()))
            {
              // Add text before the sample to be read
              if (supplier.Equals("StreamElements")) { Audio.AddToSampleProviderList(StreamElements.GetTTS(text[..index].Trim(), voice), ref newAudio); }
              else if (supplier.Equals("TikTok")) { Audio.AddToSampleProviderList(TikTok.GetTTS(text[..index].Trim(), voice), ref newAudio); }
              else { MainWindow.ConsoleWarning($">> TTS supplier {supplier} not recognized!"); }

              // Add sample sound
              Audio.AddToSampleProviderList(sampleSounds[maybeSample], ref newAudio);

              // Remove already parsed text
              if (nextIndex == -1) { text = string.Empty; } // Already reached the end
              else { text = text[nextIndex..]; }
            }
          }
        }
        else
        {
          // No sample found, add text to be read, clear the remainder of the text
          if (supplier.Equals("StreamElements")) { Audio.AddToSampleProviderList(StreamElements.GetTTS(text.Trim(), voice), ref newAudio); }
          else if (supplier.Equals("TikTok")) { Audio.AddToSampleProviderList(TikTok.GetTTS(text.Trim(), voice), ref newAudio); }
          else { MainWindow.ConsoleWarning($">> TTS supplier {supplier} not recognized!"); }
          text = string.Empty;
        }
      }

      // Insert new audio to sounds list
      for (int i = newAudio.Count - 1; i >= 0; i--)
      {
        if (newAudio[i] is null) { MainWindow.ConsoleWarning(">> Some TTS request returned null audio player!"); }
        else { sounds.Insert(0, newAudio[i]); }
      }
    }
  }
}
