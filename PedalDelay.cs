// Pedal Dly PCM41 – ReBuzz managed effect machine
// Inspired by the Lexicon PCM41 digital delay processor.
//
// Features:
//   • Delay time configurable as ms / ticks / samples with sub-sample interpolation
//   • Feedback with one-pole HF damping (tape-style warmth)
//   • Sine-wave LFO for chorus / flanging modulation (depth always in ms)
//   • Stereo ping-pong routing
//   • Wet / dry mix
//   • Zero-CPU when input and delay tail are both silent
//
// Build:
//   dotnet build PedalDlyPCM41.csproj -c Release

using System;
using Buzz.MachineInterface;

namespace WDE.PedalDlyPCM41
{
    // =========================================================================
    // Machine declaration
    // =========================================================================

    [MachineDecl(
        Name        = "Pedal Dly PCM41",
        ShortName   = "PdlPCM41",
        Author      = "WDE",
        MaxTracks   = 0,
        InputCount  = 1,
        OutputCount = 1)]
    public class PedalDlyPCM41Machine : IBuzzMachine
    {
        IBuzzMachineHost host;

        // ── Delay buffers ─────────────────────────────────────────────────────
        // Sized for 2 s at 96 kHz plus LFO headroom.
        const int MAX_BUFFER = 200000;
        readonly float[] bufL = new float[MAX_BUFFER];
        readonly float[] bufR = new float[MAX_BUFFER];
        int writePos;

        // ── One-pole LP filter state (HF damping in feedback path) ────────────
        float dampL;
        float dampR;

        // ── LFO state ─────────────────────────────────────────────────────────
        double lfoPhase;

        // ── Silence / CPU-bypass state ────────────────────────────────────────
        // When input goes silent we keep running until the delay tail has fully
        // decayed, then return false so ReBuzz stops calling us.
        //
        // Tail budget: worst case is max delay (2 s) with 99 % feedback.
        // At fb=0.99 the tail is theoretically infinite, but in practice the
        // HF damping and floating-point underflow kill it well within 60 s.
        // We use a generous 30-second headroom expressed as a sample count that
        // is recomputed each Work() from the current sample rate.
        //
        // _silentSamples counts consecutive silent-input samples.
        // When it exceeds _tailBudget the buffer will have drained and we stop.
        int _silentSamples;

        // Threshold below which a sample is considered silent (matches the
        // scale used by other Pedal machines — ±32768 domain).
        const float SILENCE_THRESHOLD = 0.001f;   // ≈ −90 dBFS

        public PedalDlyPCM41Machine(IBuzzMachineHost host) => this.host = host;

        // ── Time mode constants ───────────────────────────────────────────────
        const int MODE_MS      = 0;
        const int MODE_TICKS   = 1;
        const int MODE_SAMPLES = 2;

        // =========================================================================
        // Parameters
        // =========================================================================

        /// <summary>
        /// Selects how the Delay value is interpreted.
        /// ms      — milliseconds (1–2000)
        /// ticks   — pattern ticks, tempo-synced (1–128)
        /// samples — raw sample count (1–96000, covers 2 s at 48 kHz)
        /// </summary>
        [ParameterDecl(
            Name              = "Time Mode",
            Description       = "Units used for the Delay parameter",
            MinValue          = 0,
            MaxValue          = 2,
            DefValue          = 0,
            ValueDescriptions = new[] { "ms", "ticks", "samples" })]
        public int TimeMode { get; set; } = MODE_MS;

        /// <summary>
        /// Delay time. Interpretation depends on Time Mode:
        ///   ms      — 1–2000 ms
        ///   ticks   — 1–128 pattern ticks
        ///   samples — 1–65534 samples (≈ 1.36 s at 48 kHz)
        /// MaxValue = 65534: Word parameters are 16-bit unsigned; 65535 is
        /// reserved as the NoValue sentinel and cannot be used as MaxValue.
        /// </summary>
        [ParameterDecl(
            Name        = "Delay",
            Description = "Delay time (unit set by Time Mode)",
            MinValue    = 1,
            MaxValue    = 65534,
            DefValue    = 250)]
        public int Delay { get; set; } = 250;

        /// <summary>Amount of signal fed back into the delay line (0–99 %).</summary>
        [ParameterDecl(
            Name        = "Feedback",
            Description = "Feedback amount 0–99 %",
            MinValue    = 0,
            MaxValue    = 99,
            DefValue    = 40)]
        public int Feedback { get; set; } = 40;

        /// <summary>Wet/dry blend.  0 = fully dry, 100 = fully wet.</summary>
        [ParameterDecl(
            Name        = "Mix",
            Description = "Wet/dry mix (0 = dry, 100 = wet)",
            MinValue    = 0,
            MaxValue    = 100,
            DefValue    = 50)]
        public int Mix { get; set; } = 50;

        /// <summary>
        /// High-frequency damping applied to the feedback signal.
        /// Emulates the gentle treble roll-off of analogue BBD / tape
        /// circuits on the PCM41's input stage.
        /// 0 = flat; 100 = heavy low-pass.
        /// </summary>
        [ParameterDecl(
            Name        = "HF Damp",
            Description = "High-frequency damping in the feedback path",
            MinValue    = 0,
            MaxValue    = 100,
            DefValue    = 20)]
        public int HFDamp { get; set; } = 20;

        /// <summary>
        /// LFO rate mapped 0–100 → 0–10 Hz.
        /// Drives the PCM41-style pitch / time modulation for chorus and
        /// flanging effects.
        /// </summary>
        [ParameterDecl(
            Name        = "LFO Rate",
            Description = "Modulation rate: 0 = off, 100 ≈ 10 Hz",
            MinValue    = 0,
            MaxValue    = 100,
            DefValue    = 0)]
        public int LFORate { get; set; } = 0;

        /// <summary>LFO modulation depth mapped 0–100 → 0–25 ms.</summary>
        [ParameterDecl(
            Name        = "LFO Depth",
            Description = "Modulation depth: 0 = none, 100 = 25 ms",
            MinValue    = 0,
            MaxValue    = 100,
            DefValue    = 0)]
        public int LFODepth { get; set; } = 0;

        /// <summary>
        /// Stereo ping-pong routing: left echoes appear on the right output
        /// and vice-versa, giving a wide stereo bouncing effect.
        /// </summary>
        [ParameterDecl(
            Name              = "Ping Pong",
            Description       = "Stereo ping-pong delay routing",
            MinValue          = 0,
            MaxValue          = 1,
            DefValue          = 0,
            ValueDescriptions = new[] { "off", "on" })]
        public int PingPong { get; set; } = 0;

        // =========================================================================
        // Audio processing
        // =========================================================================

        public bool Work(Sample[] output, Sample[] input, int n, WorkModes mode)
        {
            // ReBuzz signals no connected input — nothing to do.
            if (mode == WorkModes.WM_NOIO)
                return false;

            // ── Precompute per-buffer constants ───────────────────────────────
            float sr         = host.MasterInfo.SamplesPerSec;
            float fbGain     = Feedback  / 100f;
            float wet        = Mix       / 100f;
            float dry        = 1f - wet;

            // LFO: knob 0-100 → 0-10 Hz rate, 0-25 ms depth.
            float lfoRateHz  = LFORate  / 10f;
            float lfoDepthMs = LFODepth / 4f;
            double lfoInc    = (sr > 0f) ? (lfoRateHz / sr) : 0.0;

            // ── Delay time → samples (computed once per buffer) ───────────────
            // Convert the Delay parameter to samples based on the selected mode.
            // LFO depth is always in ms regardless of mode, then converted here.
            float baseDelaySamples;
            switch (TimeMode)
            {
                case MODE_TICKS:
                    // SamplesPerTick reflects the current tempo — tempo-synced delay.
                    baseDelaySamples = Delay * (float)host.MasterInfo.SamplesPerTick;
                    break;
                case MODE_SAMPLES:
                    baseDelaySamples = (float)Delay;
                    break;
                default: // MODE_MS
                    baseDelaySamples = Delay * sr / 1000f;
                    break;
            }
            // LFO depth is always expressed in ms, converted to samples here.
            float lfoDepthSamples = lfoDepthMs * sr / 1000f;

            // One-pole LP: 0 → flat, 0.97 → heavy roll-off.
            float dampCoeff  = HFDamp / 100f * 0.97f;
            bool  pingPong   = PingPong != 0;

            // ── Silence detection — input pass ────────────────────────────────
            // Scan the input block first (cheap — no DSP).  If every sample is
            // below the threshold the block is silent.
            bool inputSilent = true;
            for (int i = 0; i < n; i++)
            {
                if (Math.Abs(input[i].L) > SILENCE_THRESHOLD ||
                    Math.Abs(input[i].R) > SILENCE_THRESHOLD)
                {
                    inputSilent = false;
                    break;
                }
            }

            if (inputSilent)
            {
                _silentSamples += n;

                // Tail budget: 30 s worth of samples covers even 99 % feedback
                // with heavy damping.  When the counter exceeds this the buffer
                // has fully decayed; stop spending CPU.
                int tailBudget = (int)(sr * 30f);
                if (_silentSamples > tailBudget)
                    return false;
            }
            else
            {
                // Live input — reset the silence counter.
                _silentSamples = 0;
            }

            // ── Sample loop ───────────────────────────────────────────────────
            for (int i = 0; i < n; i++)
            {
                // LFO sine (normalised phase 0–1)
                float lfoVal = (float)Math.Sin(lfoPhase * (2.0 * Math.PI));
                lfoPhase    += lfoInc;
                if (lfoPhase >= 1.0) lfoPhase -= 1.0;

                // Apply LFO modulation to the pre-computed base delay (in samples)
                float delaySamples = Math.Max(1f, baseDelaySamples + lfoVal * lfoDepthSamples);
                if (delaySamples >= MAX_BUFFER - 2f)
                    delaySamples = MAX_BUFFER - 2f;

                float readF = writePos - delaySamples;
                if (readF < 0f) readF += MAX_BUFFER;

                int   r0   = (int)readF % MAX_BUFFER;
                int   r1   = (r0 + 1)  % MAX_BUFFER;
                float frac = readF - (int)readF;

                // Linear interpolation of delayed signal
                float dL = bufL[r0] + frac * (bufL[r1] - bufL[r0]);
                float dR = bufR[r0] + frac * (bufR[r1] - bufR[r0]);

                // HF damping (one-pole LP in feedback path)
                dampL = dampL * dampCoeff + dL * (1f - dampCoeff);
                dampR = dampR * dampCoeff + dR * (1f - dampCoeff);

                // Write into delay buffer (with feedback)
                if (pingPong)
                {
                    bufL[writePos] = input[i].L + dampR * fbGain;
                    bufR[writePos] = input[i].R + dampL * fbGain;
                }
                else
                {
                    bufL[writePos] = input[i].L + dampL * fbGain;
                    bufR[writePos] = input[i].R + dampR * fbGain;
                }

                writePos = (writePos + 1) % MAX_BUFFER;

                // Output mix
                output[i].L = input[i].L * dry + dL * wet;
                output[i].R = input[i].R * dry + dR * wet;
            }

            return true;
        }
    }
}
