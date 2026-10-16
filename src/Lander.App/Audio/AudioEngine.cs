using System.Runtime.InteropServices;
using Lander.App.Interop;
using Lander.App.Services;
using static Lander.App.Interop.NativeConstants;

namespace Lander.App.Audio;

/// <summary>
/// Mezclador mínimo sobre waveOut: varias voces a la vez, un bucle de motor con volumen suave y
/// volumen general. Solo abre el dispositivo mientras el sonido está activado; sin sonidos en curso
/// su hilo duerme.
/// </summary>
internal sealed unsafe class AudioEngine : IDisposable
{
    private const int BufferSamples = 1024;
    private const int BufferCount = 4;

    private readonly Lock _gate = new();
    private readonly List<Voice> _voices = [];
    private readonly AutoResetEvent _signal = new(false);

    private nint _device;
    private WAVEHDR* _headers;
    private short* _memory;
    private Thread? _thread;
    private volatile bool _running;
    private float[]? _engineLoop;
    private int _enginePosition;
    private float _engineGain;
    private volatile float _engineTarget;
    private volatile float _volume = 0.5f;

    public bool IsEnabled => _running;

    /// <summary>Volumen general de 0 a 1 (se aplica con una curva y un techo bajo).</summary>
    public double Volume
    {
        get => _volume;
        set => _volume = (float)Math.Clamp(value, 0, 1);
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled == _running)
        {
            return;
        }

        if (enabled)
        {
            Open();
        }
        else
        {
            Close();
        }
    }

    public void Play(Sound sound, double gain = 1)
    {
        if (!_running)
        {
            return;
        }

        lock (_gate)
        {
            if (_voices.Count < 16)
            {
                _voices.Add(new Voice(SoundBank.Get(sound), (float)gain));
            }
        }

        _signal.Set();
    }

    /// <summary>Nivel del zumbido del motor (0 = apagado).</summary>
    public void SetEngine(double level)
    {
        _engineTarget = (float)Math.Clamp(level, 0, 1);
        if (_running && level > 0.01)
        {
            _signal.Set();
        }
    }

    public void Dispose()
    {
        Close();
        _signal.Dispose();
    }

    private void Open()
    {
        var format = new WAVEFORMATEX
        {
            wFormatTag = WAVE_FORMAT_PCM,
            nChannels = 1,
            nSamplesPerSec = SoundBank.SampleRate,
            wBitsPerSample = 16,
            nBlockAlign = 2,
            nAvgBytesPerSec = SoundBank.SampleRate * 2,
        };

        var result = Native.waveOutOpen(out _device, WAVE_MAPPER, &format, _signal.SafeWaitHandle.DangerousGetHandle(), 0, CALLBACK_EVENT);
        if (result != 0)
        {
            Log.Warn($"No se pudo abrir la salida de audio (waveOutOpen {result}).");
            _device = 0;
            return;
        }

        _engineLoop ??= SoundBank.Get(Sound.EngineLoop);
        _memory = (short*)NativeMemory.AllocZeroed((nuint)(BufferSamples * BufferCount * sizeof(short)));
        _headers = (WAVEHDR*)NativeMemory.AllocZeroed((nuint)(BufferCount * sizeof(WAVEHDR)));
        for (var i = 0; i < BufferCount; i++)
        {
            _headers[i].lpData = (byte*)(_memory + i * BufferSamples);
            _headers[i].dwBufferLength = BufferSamples * sizeof(short);
            _headers[i].dwFlags = WHDR_DONE;
            _ = Native.waveOutPrepareHeader(_device, &_headers[i], (uint)sizeof(WAVEHDR));
            _headers[i].dwFlags |= WHDR_DONE;
        }

        _running = true;
        _thread = new Thread(Run) { IsBackground = true, Name = "Lander audio", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    private void Close()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        _signal.Set();
        _thread?.Join(1000);
        _thread = null;

        _ = Native.waveOutReset(_device);
        for (var i = 0; i < BufferCount; i++)
        {
            _ = Native.waveOutUnprepareHeader(_device, &_headers[i], (uint)sizeof(WAVEHDR));
        }

        _ = Native.waveOutClose(_device);
        _device = 0;
        NativeMemory.Free(_headers);
        NativeMemory.Free(_memory);
        _headers = null;
        _memory = null;

        lock (_gate)
        {
            _voices.Clear();
        }

        _engineGain = 0;
    }

    private void Run()
    {
        var mix = new float[BufferSamples];
        while (_running)
        {
            var submitted = false;
            for (var i = 0; i < BufferCount && _running; i++)
            {
                var header = &_headers[i];
                if ((header->dwFlags & WHDR_DONE) == 0 || !HasSound())
                {
                    continue;
                }

                Mix(mix);
                var output = (short*)header->lpData;
                for (var s = 0; s < BufferSamples; s++)
                {
                    // Saturación suave: nunca recorta de golpe.
                    var x = mix[s];
                    var y = x / (1 + Math.Abs(x));
                    output[s] = (short)(y * short.MaxValue);
                }

                header->dwFlags &= ~WHDR_DONE;
                _ = Native.waveOutWrite(_device, header, (uint)sizeof(WAVEHDR));
                submitted = true;
            }

            if (!submitted)
            {
                _signal.WaitOne(500);
            }
            else
            {
                _signal.WaitOne(30);
            }
        }
    }

    private bool HasSound()
    {
        if (_engineTarget > 0.001 || _engineGain > 0.001)
        {
            return true;
        }

        lock (_gate)
        {
            return _voices.Count > 0;
        }
    }

    private void Mix(float[] mix)
    {
        Array.Clear(mix);
        var master = _volume * _volume * 0.9f;

        var loop = _engineLoop!;
        var target = _engineTarget * 0.55f;
        for (var s = 0; s < mix.Length; s++)
        {
            _engineGain += (target - _engineGain) * 0.0008f;
            if (_engineGain > 0.0005f)
            {
                mix[s] += loop[_enginePosition] * _engineGain * master;
            }

            _enginePosition = (_enginePosition + 1) % loop.Length;
        }

        lock (_gate)
        {
            for (var v = _voices.Count - 1; v >= 0; v--)
            {
                var voice = _voices[v];
                var samples = voice.Samples;
                var gain = voice.Gain * master;
                var count = Math.Min(mix.Length, samples.Length - voice.Position);
                for (var s = 0; s < count; s++)
                {
                    mix[s] += samples[voice.Position + s] * gain;
                }

                voice.Position += count;
                if (voice.Position >= samples.Length)
                {
                    _voices.RemoveAt(v);
                }
            }
        }
    }

    private sealed class Voice(float[] samples, float gain)
    {
        public float[] Samples { get; } = samples;

        public float Gain { get; } = gain;

        public int Position { get; set; }
    }
}
