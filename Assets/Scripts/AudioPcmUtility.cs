using System;
using UnityEngine;

public static class AudioPcmUtility
{
    public static byte[] ConvertFloatSamplesToPcm16(float[] samples, int frameCount, int channels, int sourceSampleRate, int targetSampleRate)
    {
        if (samples == null || samples.Length == 0 || frameCount <= 0)
        {
            return Array.Empty<byte>();
        }

        channels = Mathf.Max(1, channels);
        sourceSampleRate = Mathf.Max(1, sourceSampleRate);
        targetSampleRate = Mathf.Max(1, targetSampleRate);

        if (sourceSampleRate == targetSampleRate)
        {
            return ConvertFloatSamplesToPcm16(samples, frameCount, channels);
        }

        int sourceFrames = Mathf.Min(frameCount, samples.Length / channels);
        int targetFrames = Mathf.Max(1, Mathf.RoundToInt(sourceFrames * (targetSampleRate / (float)sourceSampleRate)));
        byte[] bytes = new byte[targetFrames * sizeof(short)];

        for (int frame = 0; frame < targetFrames; frame++)
        {
            float sourcePosition = frame * (sourceFrames - 1f) / Mathf.Max(1, targetFrames - 1);
            int leftFrame = Mathf.Clamp(Mathf.FloorToInt(sourcePosition), 0, sourceFrames - 1);
            int rightFrame = Mathf.Clamp(leftFrame + 1, 0, sourceFrames - 1);
            float t = sourcePosition - leftFrame;

            float monoSample = 0f;
            for (int channel = 0; channel < channels; channel++)
            {
                float left = samples[leftFrame * channels + channel];
                float right = samples[rightFrame * channels + channel];
                monoSample += Mathf.Lerp(left, right, t);
            }

            monoSample /= channels;
            short intSample = (short)Mathf.Clamp(monoSample * short.MaxValue, short.MinValue, short.MaxValue);
            int byteIndex = frame * sizeof(short);
            bytes[byteIndex] = (byte)(intSample & 0xFF);
            bytes[byteIndex + 1] = (byte)((intSample >> 8) & 0xFF);
        }

        return bytes;
    }

    public static byte[] ConvertFloatSamplesToPcm16(float[] samples, int frameCount, int channels)
    {
        if (samples == null || samples.Length == 0 || frameCount <= 0)
        {
            return Array.Empty<byte>();
        }

        channels = Mathf.Max(1, channels);
        int sampleCount = Mathf.Min(samples.Length, frameCount * channels);
        int pcmSampleCount = Mathf.CeilToInt(sampleCount / (float)channels);
        byte[] bytes = new byte[pcmSampleCount * sizeof(short)];

        int byteIndex = 0;
        for (int frame = 0; frame < pcmSampleCount; frame++)
        {
            float monoSample = 0f;

            for (int channel = 0; channel < channels; channel++)
            {
                int sampleIndex = frame * channels + channel;
                if (sampleIndex >= sampleCount)
                {
                    break;
                }

                monoSample += samples[sampleIndex];
            }

            monoSample /= channels;
            short intSample = (short)Mathf.Clamp(monoSample * short.MaxValue, short.MinValue, short.MaxValue);

            bytes[byteIndex++] = (byte)(intSample & 0xFF);
            bytes[byteIndex++] = (byte)((intSample >> 8) & 0xFF);
        }

        return bytes;
    }

    public static float[] ConvertPcm16BytesToFloats(byte[] pcmBytes)
    {
        if (pcmBytes == null || pcmBytes.Length == 0)
        {
            return Array.Empty<float>();
        }

        int sampleCount = pcmBytes.Length / sizeof(short);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            short value = (short)(pcmBytes[i * 2] | (pcmBytes[i * 2 + 1] << 8));
            samples[i] = Mathf.Clamp(value / 32768f, -1f, 1f);
        }

        return samples;
    }

    public static bool TryExtractPcm16FromWav(byte[] wavBytes, out byte[] pcmBytes)
    {
        pcmBytes = Array.Empty<byte>();

        if (wavBytes == null || wavBytes.Length < 44)
        {
            return false;
        }

        if (!HasAsciiTag(wavBytes, 0, "RIFF") || !HasAsciiTag(wavBytes, 8, "WAVE"))
        {
            return false;
        }

        int offset = 12;
        int dataOffset = -1;
        int dataLength = 0;
        short audioFormat = 0;
        short bitsPerSample = 0;

        while (offset + 8 <= wavBytes.Length)
        {
            string chunkId = System.Text.Encoding.ASCII.GetString(wavBytes, offset, 4);
            int chunkSize = BitConverter.ToInt32(wavBytes, offset + 4);
            int chunkDataOffset = offset + 8;

            if (chunkDataOffset + chunkSize > wavBytes.Length)
            {
                break;
            }

            if (chunkId == "fmt ")
            {
                audioFormat = BitConverter.ToInt16(wavBytes, chunkDataOffset);
                bitsPerSample = BitConverter.ToInt16(wavBytes, chunkDataOffset + 14);
            }
            else if (chunkId == "data")
            {
                dataOffset = chunkDataOffset;
                dataLength = chunkSize;
                break;
            }

            offset = chunkDataOffset + chunkSize;
            if ((chunkSize & 1) == 1)
            {
                offset++;
            }
        }

        if (dataOffset < 0 || dataLength <= 0)
        {
            return false;
        }

        if (audioFormat != 1 || bitsPerSample != 16)
        {
            return false;
        }

        pcmBytes = new byte[dataLength];
        Buffer.BlockCopy(wavBytes, dataOffset, pcmBytes, 0, dataLength);
        return true;
    }

    private static bool HasAsciiTag(byte[] bytes, int offset, string tag)
    {
        if (bytes == null || tag == null || offset < 0 || offset + tag.Length > bytes.Length)
        {
            return false;
        }

        for (int i = 0; i < tag.Length; i++)
        {
            if (bytes[offset + i] != (byte)tag[i])
            {
                return false;
            }
        }

        return true;
    }
}
