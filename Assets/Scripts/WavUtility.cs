using System;
using System.IO;
using UnityEngine;

public static class WavUtility
{
    public static byte[] FromAudioClip(AudioClip clip, int sampleCount = -1, int targetSampleRate = 16000, int targetChannels = 1)
    {
        if (clip == null)
        {
            return Array.Empty<byte>();
        }

        int sourceChannels = clip.channels;
        int sourceSampleRate = clip.frequency;
        int totalFrames = clip.samples;
        int validFrames = sampleCount > 0 ? Mathf.Min(sampleCount, totalFrames) : totalFrames;

        float[] samples = new float[totalFrames * sourceChannels];
        clip.GetData(samples, 0);

        if (targetSampleRate <= 0)
        {
            targetSampleRate = sourceSampleRate;
        }

        if (targetChannels <= 0)
        {
            targetChannels = 1;
        }

        int outputFrames = Mathf.Max(1, Mathf.FloorToInt((validFrames / (float)sourceSampleRate) * targetSampleRate));

        using (var memoryStream = new MemoryStream())
        using (var writer = new BinaryWriter(memoryStream))
        {
            WriteWavHeader(writer, outputFrames, targetChannels, targetSampleRate);

            for (int outputFrame = 0; outputFrame < outputFrames; outputFrame++)
            {
                float sourceTime = outputFrame / (float)targetSampleRate;
                int sourceFrame = Mathf.Clamp(Mathf.FloorToInt(sourceTime * sourceSampleRate), 0, validFrames - 1);
                int sourceBaseIndex = sourceFrame * sourceChannels;
                float monoSample = 0f;

                for (int channel = 0; channel < sourceChannels; channel++)
                {
                    monoSample += samples[sourceBaseIndex + channel];
                }

                monoSample /= sourceChannels;

                for (int channel = 0; channel < targetChannels; channel++)
                {
                    short intSample = (short)Mathf.Clamp(monoSample * short.MaxValue, short.MinValue, short.MaxValue);
                    writer.Write(intSample);
                }
            }

            writer.Flush();
            return memoryStream.ToArray();
        }
    }

    private static void WriteWavHeader(BinaryWriter writer, int sampleCount, int channels, int frequency)
    {
        const short bitsPerSample = 16;
        short blockAlign = (short)(channels * bitsPerSample / 8);
        int byteRate = frequency * blockAlign;
        int dataSize = sampleCount * blockAlign;

        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(frequency);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);
    }
}
