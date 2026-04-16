# Pedal Dly PCM41

A managed effect machine for [ReBuzz](https://github.com/wasteddesign/ReBuzz) inspired by the classic **Lexicon PCM41** digital delay processor.

## Features

| Feature | Detail |
|---|---|
| Delay time | 1 – 2000 ms with sub-sample linear interpolation |
| Feedback | 0 – 99 % with optional HF damping |
| HF Damp | One-pole low-pass filter in the feedback path — emulates the gentle treble roll-off of BBD / tape circuits |
| LFO modulation | Sine-wave LFO (0–10 Hz) modulates delay time for chorus and flanging effects |
| Ping Pong | Stereo cross-feed routing — left echoes pan right and vice-versa |
| Wet / Dry mix | Full control from 100 % dry to 100 % wet |

## Requirements

- [ReBuzz](https://github.com/wasteddesign/ReBuzz) (1812-preview or later)
- [.NET 10.0 Desktop Runtime (Windows x64)](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [.NET 10.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) — to build from source

## Installation

1. Build from source (see below), **or** copy the pre-built `Pedal Dly PCM41.NET.dll` directly.
2. Place `Pedal Dly PCM41.NET.dll` in your ReBuzz Effects gear folder:
   ```
   C:\Program Files\ReBuzz\Gear\Effects\
   ```
3. Restart ReBuzz. **Pedal Dly PCM41** will appear in the Effects section of the machine list.

## Building from source

```powershell
dotnet build PedalDlyPCM41.csproj -c Release
```

The DLL is written directly to `C:\Program Files\ReBuzz\Gear\Effects\`.

If ReBuzz is installed in a non-default location, pass the path on the command line:

```powershell
dotnet build PedalDlyPCM41.csproj -c Release /p:BuzzDir="D:\MyReBuzz"
```

## Parameters

| Parameter | Range | Default | Description |
|---|---|---|---|
| Delay | 1 – 2000 ms | 250 | Delay time in milliseconds |
| Feedback | 0 – 99 % | 40 | Amount of delayed signal fed back |
| Mix | 0 – 100 % | 50 | Wet/dry blend (0 = dry, 100 = wet) |
| HF Damp | 0 – 100 | 20 | High-frequency roll-off in feedback path |
| LFO Rate | 0 – 100 | 0 | Modulation rate (0 = off, 100 ≈ 10 Hz) |
| LFO Depth | 0 – 100 | 0 | Modulation depth (0 = none, 100 = ±25 ms) |
| Ping Pong | off / on | off | Stereo ping-pong cross-feed routing |

## PCM41 Design Notes

The **Lexicon PCM41** (1981) was one of the first affordable rack-mount digital delay processors and became famous for its pitch-shifting capabilities and warm delay character. This plugin captures its key sonic attributes:

- **HF Damp** mimics the gentle treble loss that occurs on each pass through the PCM41's analogue input stage — keeping long feedback tails from becoming harsh.
- **LFO modulation** reproduces the PCM41's pitch-modulation circuit, which could turn a short delay into a rich chorus or flange with a single knob sweep.
- **Ping Pong** extends the stereo field in a way characteristic of how engineers used two PCM41 units in sequence.

## License

MIT
