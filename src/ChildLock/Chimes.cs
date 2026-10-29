using System.Media;

namespace ChildLock;

/// <summary>Two short tones, generated in memory so no sound files ship with the app.</summary>
internal static class Chimes
{
    static readonly byte[] LockWav = TwoTone(880, 660);
    static readonly byte[] UnlockWav = TwoTone(660, 880);

    public static void PlayLock() => Play(LockWav);
    public static void PlayUnlock() => Play(UnlockWav);

    static void Play(byte[] wav)
    {
        try
        {
            using var player = new SoundPlayer(new MemoryStream(wav));
            player.Play();
        }
        catch (InvalidOperationException)
        {
            // No audio device; a missing chime is not worth bothering anyone about.
        }
    }

    static byte[] TwoTone(double firstHz, double secondHz)
    {
        const int rate = 44100;
        const double toneSeconds = 0.09;
        int toneSamples = (int)(rate * toneSeconds);
        var samples = new short[toneSamples * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            double hz = i < toneSamples ? firstHz : secondHz;
            int j = i % toneSamples;
            double envelope = Math.Min(1, Math.Min(j, toneSamples - j) / (rate * 0.01));
            samples[i] = (short)(Math.Sin(2 * Math.PI * hz * i / rate) * envelope * 0.25 * short.MaxValue);
        }

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int dataBytes = samples.Length * 2;
        w.Write("RIFF"u8);
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(rate);
        w.Write(rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(dataBytes);
        foreach (var s in samples) w.Write(s);
        w.Flush();
        return ms.ToArray();
    }
}
