using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Original, deterministic PCM sound palette; no external sound library or runtime synthesis.</summary>
public static class FeedbackAudioAuthoring
{
    const int SampleRate = 44100;
    const string OutputFolder = "Assets/Resources/Feedback/Audio";

    [MenuItem("Tools/Grove/Rebuild Interaction Sounds")]
    public static void Rebuild()
    {
        Directory.CreateDirectory(OutputFolder);
        foreach (FeedbackCue cue in Enum.GetValues(typeof(FeedbackCue)))
            WriteWave(OutputFolder + "/" + cue + ".wav", Compose(cue));
        AssetDatabase.Refresh();
        foreach (FeedbackCue cue in Enum.GetValues(typeof(FeedbackCue)))
        {
            var importer = AssetImporter.GetAtPath(OutputFolder + "/" + cue + ".wav") as AudioImporter;
            if (importer == null) continue;
            importer.forceToMono = true;
            var settings = importer.defaultSampleSettings;
            settings.preloadAudioData = true;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.PCM;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
        }
        Debug.Log("GROVE_FEEDBACK_AUDIO_READY: " + Enum.GetValues(typeof(FeedbackCue)).Length + " original interaction sounds.");
    }

    static float[] Compose(FeedbackCue cue)
    {
        float duration = cue == FeedbackCue.GameComplete ? 2.2f
            : cue == FeedbackCue.Heal || cue == FeedbackCue.Reunion ? 1.3f
            : cue == FeedbackCue.Brew || cue == FeedbackCue.Success || cue == FeedbackCue.Reward ? 1.05f
            : cue == FeedbackCue.Recall || cue == FeedbackCue.Teleport ? 0.65f
            : cue == FeedbackCue.Harvest ? 0.5f : 0.32f;
        var samples = new float[Mathf.CeilToInt(duration * SampleRate)];
        switch (cue)
        {
            case FeedbackCue.Focus:
            case FeedbackCue.UIHover:
                Pluck(samples, 0f, 1174.66f, 0.12f, 0.3f); break;
            case FeedbackCue.Dialogue:
                Pluck(samples, 0f, 587.33f, 0.08f, 0.5f);
                Noise(samples, 0f, 0.045f, 0.08f, 1, 0.25f); break;
            case FeedbackCue.Pickup:
                Pluck(samples, 0f, 659.25f, 0.18f, 0.6f);
                Pluck(samples, 0.065f, 987.77f, 0.22f, 0.35f); break;
            case FeedbackCue.Drop:
            case FeedbackCue.Impact:
                Sweep(samples, 0f, 0.16f, 170f, 68f, 0.7f);
                Noise(samples, 0f, 0.085f, 0.15f, 2, 0.08f); break;
            case FeedbackCue.Throw:
                Noise(samples, 0f, 0.25f, 0.65f, 3, 0.13f);
                Sweep(samples, 0.025f, 0.18f, 300f, 650f, 0.09f); break;
            case FeedbackCue.Recall:
            case FeedbackCue.Teleport:
                Noise(samples, 0f, 0.38f, 0.3f, 4, 0.08f);
                Sweep(samples, 0f, 0.4f, 330f, 880f, 0.16f);
                Pluck(samples, 0.3f, 1318.51f, 0.3f, 0.3f); break;
            case FeedbackCue.Place:
                Pluck(samples, 0f, 440f, 0.12f, 0.5f);
                Noise(samples, 0f, 0.07f, 0.12f, 5, 0.3f);
                Pluck(samples, 0.05f, 659.25f, 0.2f, 0.24f); break;
            case FeedbackCue.Harvest:
                for (int i = 0; i < 5; i++)
                    Noise(samples, i * 0.045f, 0.13f, 0.28f, i + 20, 0.7f);
                Pluck(samples, 0.08f, 1318.51f, 0.28f, 0.16f); break;
            case FeedbackCue.Brew:
                for (int i = 0; i < 7; i++)
                {
                    Noise(samples, i * 0.085f, 0.11f, 0.22f, i + 30, 0.22f);
                    Sweep(samples, i * 0.09f, 0.09f, 280f + i * 50f, 160f + i * 20f, 0.25f);
                }
                Pluck(samples, 0.58f, 880f, 0.35f, 0.3f);
                Pluck(samples, 0.69f, 1318.51f, 0.3f, 0.22f); break;
            case FeedbackCue.Heal:
                Noise(samples, 0f, 0.65f, 0.12f, 8, 0.04f);
                Chord(samples, new[] { 523.25f, 659.25f, 783.99f }, 0.1f, 0.14f, 0.8f); break;
            case FeedbackCue.Reward:
                Chord(samples, new[] { 659.25f, 987.77f, 1318.51f }, 0f, 0.12f, 0.6f); break;
            case FeedbackCue.Success:
                Chord(samples, new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0f, 0.1f, 0.6f); break;
            case FeedbackCue.Surprise:
                Sweep(samples, 0f, 0.22f, 440f, 1200f, 0.3f);
                Noise(samples, 0f, 0.08f, 0.15f, 9, 0.45f); break;
            case FeedbackCue.Reunion:
                Chord(samples, new[] { 392f, 523.25f, 659.25f, 783.99f }, 0f, 0.14f, 0.8f); break;
            case FeedbackCue.UIClick:
                Pluck(samples, 0f, 783.99f, 0.11f, 0.5f);
                Pluck(samples, 0.04f, 1046.5f, 0.14f, 0.25f); break;
            case FeedbackCue.GameComplete:
                Chord(samples, new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0f, 0.13f, 1.1f);
                Chord(samples, new[] { 523.25f, 783.99f, 1318.51f }, 0.83f, 0.03f, 1.2f); break;
            case FeedbackCue.Blocked:
                Pluck(samples, 0f, 293.66f, 0.12f, 0.35f);
                Pluck(samples, 0.07f, 261.63f, 0.14f, 0.25f); break;
        }
        return samples;
    }

    static void Chord(float[] samples, float[] notes, float start, float spacing, float decay)
    {
        for (int i = 0; i < notes.Length; i++)
            Pluck(samples, start + i * spacing, notes[i], decay, 0.3f);
    }

    static void Pluck(float[] samples, float start, float frequency, float decay, float gain)
    {
        int offset = (int)(start * SampleRate);
        for (int i = offset; i < samples.Length; i++)
        {
            float t = (i - offset) / (float)SampleRate;
            float attack = Mathf.Min(1f, t / 0.004f);
            float phase = 2f * Mathf.PI * frequency * t;
            float signal = Mathf.Sin(phase) * Mathf.Exp(-6f * t / decay)
                + 0.24f * Mathf.Sin(phase * 2.005f) * Mathf.Exp(-10f * t / decay)
                + 0.08f * Mathf.Sin(phase * 3.01f) * Mathf.Exp(-16f * t / decay);
            samples[i] += signal * gain * attack;
        }
    }

    static void Sweep(float[] samples, float start, float duration, float from, float to, float gain)
    {
        int offset = (int)(start * SampleRate);
        int length = (int)(duration * SampleRate);
        for (int j = 0; j < length && offset + j < samples.Length; j++)
        {
            float t = j / (float)SampleRate;
            float envelope = Mathf.Sin(Mathf.PI * j / length) * Mathf.Exp(-3f * t / duration);
            float phase = 2f * Mathf.PI * (from * t + (to - from) * t * t / (2f * duration));
            samples[offset + j] += Mathf.Sin(phase) * envelope * gain;
        }
    }

    static void Noise(float[] samples, float start, float duration, float gain, int seed, float brightness)
    {
        var random = new System.Random(seed);
        int offset = (int)(start * SampleRate);
        int length = (int)(duration * SampleRate);
        float filtered = 0f;
        for (int j = 0; j < length && offset + j < samples.Length; j++)
        {
            filtered = Mathf.Lerp(filtered, (float)random.NextDouble() * 2f - 1f, brightness);
            float envelope = Mathf.Sin(Mathf.PI * j / length);
            samples[offset + j] += filtered * envelope * envelope * gain;
        }
    }

    static void WriteWave(string path, float[] samples)
    {
        float peak = 0.001f;
        foreach (float value in samples) peak = Mathf.Max(peak, Mathf.Abs(value));
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples.Length * 2);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(SampleRate); writer.Write(SampleRate * 2);
            writer.Write((short)2); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(samples.Length * 2);
            for (int i = 0; i < samples.Length; i++)
            {
                // Short fades remove clicks without softening the interaction's onset.
                float fade = Mathf.Min(1f, Mathf.Min(i, samples.Length - i - 1) / 132f);
                writer.Write((short)(Mathf.Clamp(samples[i] / peak * 0.72f * fade, -1f, 1f) * short.MaxValue));
            }
        }
    }
}
