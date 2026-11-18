# S Pen Tablet: Build Guide

Turn a Samsung Galaxy S26 Ultra + S Pen into a pressure-sensitive drawing tablet for a Windows 11 PC.

**Version 1 scope (what this guide builds):**
- The phone is a **blank drawing pad** (like a Wacom Intuos). You look at your monitor while you draw.
- It connects over a **USB cable** (Wi-Fi comes later).
- Pressure, hover, the side button and (if the phone reports it) tilt all reach the PC.
- It works in **Krita** (and any other app that supports Windows Ink) **without a custom driver**.

---

## Contents

1. [The big picture](#1-the-big-picture)
2. [Tools you need](#2-tools-you-need)
3. [Project layout](#3-project-layout)
4. [The protocol (how phone and PC talk)](#4-the-protocol)
5. [Android app](#5-android-app)
6. [Windows app](#6-windows-app)
7. [Setting up Windows & Krita](#7-setting-up-windows--krita)
8. [Milestones & how to test each one](#8-milestones--how-to-test-each-one)
9. [Gotchas (read this when something is weird)](#9-gotchas)
10. [Later: Wi-Fi, screen mirroring, real driver](#10-later-wi-fi-screen-mirroring-real-driver)
11. [Glossary](#11-glossary)

---

## 1. The big picture

```
 ┌──────────────── PHONE (Android app, Kotlin) ────────────────┐
 │  PadView                PenLink (background thread)         │
 │  reads S Pen events ──► packs each one into 24 bytes ──► TCP│──┐
 │  (x, y, pressure,       sends to 127.0.0.1:47800            │  │
 │   tilt, hover, button)                                      │  │
 └─────────────────────────────────────────────────────────────┘  │
                                                                  │ USB cable
                            "adb reverse" tunnels the phone's     │ (ADB tunnel)
                            port 47800 to the PC's port 47800     │
                                                                  │
 ┌──────────────── PC (Windows tray app, C#) ──────────────────┐  │
 │  PenServer (listens on 127.0.0.1:47800)  ◄──────────────────│──┘
 │     │ decode packet                                          │
 │     ▼                                                        │
 │  AreaMapper:    phone 0..1 coords  ──► monitor pixels        │
 │  PressureCurve: raw pressure       ──► your pressure feel    │
 │     │                                                        │
 │     ▼                                                        │
 │  PenInjector ──► Windows "synthetic pen" API ──► Windows Ink │──► Krita sees a real pen
 └──────────────────────────────────────────────────────────────┘
```

### Why these choices

| Decision | Choice | Why |
|---|---|---|
| Phone language | **Kotlin** | Android's main language. It's close to Java, which you may have seen in Minecraft modding. |
| PC language | **C# (.NET 10)** with **WinForms** | Easy to read. Calling Windows APIs from it is simple, and a tray icon takes a few lines. |
| Connection | **TCP over USB via `adb reverse`** | About 1–3 ms of delay. No root, no Wi-Fi setup, very reliable. ADB comes with Android Studio. |
| Pen on Windows | **`CreateSyntheticPointerDevice` / `InjectSyntheticPointerInput`** | Built into Windows 10 (1809) and later. It creates a *real pen* that has pressure, hover, the barrel button and the eraser. **No driver, no signing.** Krita reads it through "Windows Ink". |
| Phone UI | A single custom `View` | The simplest way to get raw stylus events with the lowest delay. |

### What happens during one pen stroke

1. Your S Pen touches the glass. Android creates a `MotionEvent` (`ACTION_DOWN`, tool type = stylus, pressure = 0.31).
2. `PadView` turns it into a `PenSample` with coordinates from 0 to 1. Then `PenLink` packs it into a 24-byte packet and puts it in a queue.
3. A background thread writes the packet to a TCP socket at `127.0.0.1:47800`. Because of `adb reverse`, that socket actually leads to the PC.
4. On the PC, `PenServer` reads exactly 24 bytes and decodes them.
5. `AreaMapper` converts (0.42, 0.66) into, say, pixel (806, 713) on your chosen monitor. `PressureCurve` converts 0.31 into Windows' range of 0–1024.
6. `PenInjector` calls `InjectSyntheticPointerInput` with "pen down at (806, 713), pressure 317".
7. Windows delivers it to Krita exactly as if a Wacom pen had touched down.

Steps 1–7 should take well under 10 ms.

---

## 2. Tools you need

### On the PC
1. **Android Studio** (latest stable): https://developer.android.com/studio
   This also installs the Android SDK and **ADB**. ADB normally lands in
   `%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe`.
2. **.NET 10 SDK**: https://dotnet.microsoft.com/download
3. An editor for the C# side. Pick **one**:
   - **Visual Studio Community** (the purple one, easiest for beginners), with the ".NET desktop development" workload, **or**
   - **VS Code** with the "C# Dev Kit" extension.
4. **Krita**: https://krita.org
5. Optional but recommended: **Git**, so you can undo mistakes: https://git-scm.com

### On the phone (one-time setup)
1. **Turn on Developer options:** Settings → About phone → Software information → tap **Build number** 7 times.
2. **Turn on USB debugging:** Settings → Developer options → **USB debugging** → On.
3. Plug the phone into the PC with a **data-capable** USB-C cable. The phone asks "Allow USB debugging?" Tick "Always allow from this computer" and tap **Allow**.
4. Check that it worked. Open a terminal on the PC and run:

   ```bash
   adb devices
   ```

   You should see one line ending in `device`. If it says `unauthorized`, look at the phone for the prompt. If `adb` isn't found, use its full path from step 1 above.
5. **Recommended:** if your S Pen settings have **Air command** (the menu that pops up when you press the pen button while hovering), turn it **off**. Look under Settings → Advanced features → S Pen. Otherwise the side button opens Samsung's menu instead of reaching your app.

---

## 3. Project layout

```
SPenTablet/
├── README.md
├── docs/
│   └── GUIDE.md                  ← this file
├── android/                      ← Android Studio project (phone app)
│   ├── settings.gradle.kts
│   ├── build.gradle.kts
│   └── app/
│       ├── build.gradle.kts
│       └── src/main/
│           ├── AndroidManifest.xml
│           └── java/com/spentablet/pad/
│               ├── MainActivity.kt   ← full-screen setup, starts the link
│               ├── PadView.kt        ← reads S Pen events
│               ├── PenSample.kt      ← one pen reading (data class)
│               ├── Protocol.kt       ← packs samples into bytes
│               └── PenLink.kt        ← TCP connection + reconnect
└── windows/                      ← .NET solution (PC app)
    ├── SPenTablet.sln
    └── SPenTablet.Host/
        ├── SPenTablet.Host.csproj
        ├── Program.cs              ← entry point, single-instance check
        ├── TrayApp.cs              ← tray icon + menu
        ├── AppSettings.cs          ← saves settings to JSON
        ├── Net/
        │   ├── Protocol.cs         ← decodes packets (mirror of Protocol.kt)
        │   └── PenServer.cs        ← TCP listener thread
        ├── Input/
        │   ├── NativeMethods.cs    ← Windows API declarations
        │   └── PenInjector.cs      ← pen state machine → Windows
        ├── Mapping/
        │   ├── AreaMapper.cs       ← phone coords → monitor pixels
        │   └── PressureCurve.cs    ← pressure feel
        └── Adb/
            └── AdbHelper.cs        ← finds adb.exe, runs "adb reverse"
```

**Rule of thumb:** each file does one job. If you're unsure where something goes, ask "is this about *reading the pen*, *sending bytes*, *mapping*, or *talking to Windows*?"

### Creating the projects

**Android:** in Android Studio, choose File → New → New Project → **Empty Views Activity**. Use these settings:
- Name: `SPen Pad`
- Package name: `com.spentablet.pad`
- Save location: `...\Documents\SPenTablet\android`
- Language: **Kotlin**
- Minimum SDK: **API 30 (Android 11)**

Pick *Empty **Views** Activity*, not the plain *Empty Activity* (that one uses Jetpack Compose). A classic `View` gives the most direct access to stylus events.

**Windows:** run these in a terminal inside `SPenTablet`:

```bash
dotnet new winforms -n SPenTablet.Host -o windows/SPenTablet.Host
```

```bash
dotnet new sln -n SPenTablet -o windows
```

```bash
dotnet sln windows/SPenTablet.slnx add windows/SPenTablet.Host
```

(Newer .NET creates `SPenTablet.slnx` instead of `.sln`. Either is fine; use whichever file appeared.)

---

## 4. The protocol

The **protocol** is the agreed format of the bytes the phone and PC send each other. Both sides must match exactly. Every packet is **exactly 24 bytes** and **little-endian**, which makes reading trivial: "read 24 bytes, look at byte 0."

| Type byte | Name | Direction | Meaning |
|---|---|---|---|
| `0x01` | HELLO | phone → PC | First packet: "I'm the pad app, my drawing area is W×H pixels" |
| `0x81` | HELLO_ACK | PC → phone | "Got it, I'm really here." Needed because of an `adb reverse` quirk ([Gotchas](#9-gotchas)) |
| `0x02` | PEN | phone → PC | One pen sample |
| `0x04` | PING | phone → PC | Sent every 0.5 s when idle, so the PC can tell the cable is still connected |

**HELLO layout**

| Offset | Size | Field |
|---|---|---|
| 0 | 1 | type = `0x01` |
| 1 | 1 | protocol version = `1` |
| 2 | 2 | reserved (0) |
| 4 | 4 | magic = `0x42545053` (the letters "SPTB") |
| 8 | 4 | pad width in pixels (int32) |
| 12 | 4 | pad height in pixels (int32) |
| 16 | 8 | reserved (0) |

**PEN layout**

| Offset | Size | Field |
|---|---|---|
| 0 | 1 | type = `0x02` |
| 1 | 1 | flags: bit0 = in range (hovering or touching), bit1 = touching, bit2 = side button, bit3 = eraser |
| 2 | 2 | sequence number (uint16, wraps around; useful for spotting dropped packets) |
| 4 | 4 | x, float32, 0.0 = left edge, 1.0 = right edge |
| 8 | 4 | y, float32, 0.0 = top, 1.0 = bottom |
| 12 | 4 | pressure, float32, 0.0–1.0 |
| 16 | 1 | tiltX, int8, degrees −90…90 |
| 17 | 1 | tiltY, int8, degrees −90…90 |
| 18 | 2 | reserved (0) |
| 20 | 4 | phone timestamp in ms (uint32) |

The phone sends **normalized** coordinates (0 to 1) rather than pixels. That way the PC can map them to any monitor without caring about the phone's resolution.

---

## 5. Android app

### 5.1 `AndroidManifest.xml` (the important parts)

```xml
<manifest xmlns:android="http://schemas.android.com/apk/res/android">

    <!-- Needed for ANY socket, even to 127.0.0.1 -->
    <uses-permission android:name="android.permission.INTERNET" />

    <application
        android:label="SPen Pad"
        android:theme="@style/Theme.SPenPad">   <!-- keep whatever theme the template made -->

        <activity
            android:name=".MainActivity"
            android:exported="true"
            android:screenOrientation="sensorLandscape">  <!-- v1: landscape only -->
            <intent-filter>
                <action android:name="android.intent.action.MAIN" />
                <category android:name="android.intent.category.LAUNCHER" />
            </intent-filter>
        </activity>
    </application>
</manifest>
```

### 5.2 `PenSample.kt`: one pen reading

```kotlin
package com.spentablet.pad

/** One moment of pen state. Coordinates are 0..1 across the pad. */
data class PenSample(
    val x: Float,
    val y: Float,
    val pressure: Float,     // 0..1, always 0 while hovering
    val tiltX: Int,          // degrees, -90..90
    val tiltY: Int,
    val inRange: Boolean,    // pen is hovering OR touching
    val inContact: Boolean,  // pen tip is touching the glass
    val barrel: Boolean,     // side button held
    val eraser: Boolean,
    val timeMs: Long,
)
```

### 5.3 `Protocol.kt`: packing bytes

```kotlin
package com.spentablet.pad

import java.nio.ByteBuffer
import java.nio.ByteOrder

object Protocol {
    const val PORT = 47800
    const val SIZE = 24
    const val VERSION: Byte = 1
    const val MAGIC = 0x42545053 // "SPTB"

    const val T_HELLO: Byte = 0x01
    const val T_PEN: Byte = 0x02
    const val T_PING: Byte = 0x04
    const val T_HELLO_ACK: Byte = 0x81.toByte()

    private const val F_IN_RANGE = 1
    private const val F_CONTACT = 2
    private const val F_BARREL = 4
    private const val F_ERASER = 8

    private fun buf() = ByteBuffer.allocate(SIZE).order(ByteOrder.LITTLE_ENDIAN)

    fun hello(width: Int, height: Int): ByteArray = buf().apply {
        put(T_HELLO); put(VERSION); putShort(0)
        putInt(MAGIC); putInt(width); putInt(height)
    }.array()

    fun ping(): ByteArray = buf().apply { put(T_PING) }.array()

    fun pen(s: PenSample, seq: Int): ByteArray {
        var flags = 0
        if (s.inRange) flags = flags or F_IN_RANGE
        if (s.inContact) flags = flags or F_CONTACT
        if (s.barrel) flags = flags or F_BARREL
        if (s.eraser) flags = flags or F_ERASER
        return buf().apply {
            put(T_PEN); put(flags.toByte()); putShort(seq.toShort())
            putFloat(s.x); putFloat(s.y); putFloat(s.pressure)
            put(s.tiltX.toByte()); put(s.tiltY.toByte()); putShort(0)
            putInt(s.timeMs.toInt())
        }.array()
    }
}
```

### 5.4 `PadView.kt`: reading the S Pen

This is the heart of the phone app. Key ideas:

- **Touching** events arrive in `onTouchEvent`. **Hover** events (pen near the glass but not touching) arrive in `onGenericMotionEvent`.
- **Palm rejection:** we only accept pointers whose tool type is `TOOL_TYPE_STYLUS` or `TOOL_TYPE_ERASER`. Fingers and palms are swallowed and ignored.
- **Batching:** Android groups several pen readings into one event to save power. The extra ones are "historical" samples. We send **all** of them, otherwise fast strokes look jagged.
- **Unbuffered dispatch:** `requestUnbufferedDispatch` asks Android to deliver events immediately instead of once per screen refresh. Lower latency.
- **HOVER_EXIT quirk:** Android sends `HOVER_EXIT` right *before* the pen touches down. If we told the PC "pen left!" every time, Krita would see the pen flicker out of range at the start of every stroke. So we wait 80 ms. If a touch or hover arrives in that time, we cancel the "left" message.

```kotlin
package com.spentablet.pad

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.view.InputDevice
import android.view.MotionEvent
import android.view.View
import kotlin.math.atan
import kotlin.math.cos
import kotlin.math.roundToInt
import kotlin.math.sin
import kotlin.math.tan

class PadView(
    context: Context,
    private val onSample: (PenSample) -> Unit,
) : View(context) {

    /** Text shown at the top of the pad (connection status). */
    var status: String = "Starting…"
        set(value) { field = value; invalidate() }

    private var last: PenSample? = null
    private val textPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = Color.GRAY; textSize = 42f }
    private val dotPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = Color.WHITE }

    private val sendOutOfRange = Runnable {
        val s = last ?: return@Runnable
        emit(s.copy(inRange = false, inContact = false, pressure = 0f))
    }

    init {
        setBackgroundColor(Color.BLACK)
        // Deliver pointer events (touch + hover) as soon as they happen.
        requestUnbufferedDispatch(InputDevice.SOURCE_CLASS_POINTER)
    }

    // ---- Pen touching the glass -------------------------------------------
    override fun onTouchEvent(e: MotionEvent): Boolean {
        val p = penIndex(e) ?: return true // finger or palm: swallow it, do nothing
        removeCallbacks(sendOutOfRange)
        val lifted = when (e.actionMasked) {
            MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> true
            MotionEvent.ACTION_POINTER_UP -> e.actionIndex == p
            else -> false
        }
        emitAll(e, p, inContact = !lifted)
        return true
    }

    // ---- Pen hovering above the glass -------------------------------------
    override fun onGenericMotionEvent(e: MotionEvent): Boolean {
        val p = penIndex(e) ?: return super.onGenericMotionEvent(e)
        when (e.actionMasked) {
            MotionEvent.ACTION_HOVER_ENTER, MotionEvent.ACTION_HOVER_MOVE -> {
                removeCallbacks(sendOutOfRange)
                emitAll(e, p, inContact = false)
            }
            MotionEvent.ACTION_HOVER_EXIT -> postDelayed(sendOutOfRange, 80)
        }
        return true
    }

    /** Index of the stylus pointer in this event, or null if there is none. */
    private fun penIndex(e: MotionEvent): Int? =
        (0 until e.pointerCount).firstOrNull {
            val t = e.getToolType(it)
            t == MotionEvent.TOOL_TYPE_STYLUS || t == MotionEvent.TOOL_TYPE_ERASER
        }

    /** Send the batched (historical) samples first, then the current one. */
    private fun emitAll(e: MotionEvent, p: Int, inContact: Boolean) {
        for (k in 0 until e.historySize) emit(sampleAt(e, p, k, inContact))
        emit(sampleAt(e, p, -1, inContact))
    }

    /** k = history index, or -1 for the event's current values. */
    private fun sampleAt(e: MotionEvent, p: Int, k: Int, inContact: Boolean): PenSample {
        val h = k >= 0
        val x = if (h) e.getHistoricalX(p, k) else e.getX(p)
        val y = if (h) e.getHistoricalY(p, k) else e.getY(p)
        val pressure = if (h) e.getHistoricalPressure(p, k) else e.getPressure(p)
        val tilt = if (h) e.getHistoricalAxisValue(MotionEvent.AXIS_TILT, p, k)
                   else e.getAxisValue(MotionEvent.AXIS_TILT, p)
        val orientation = if (h) e.getHistoricalOrientation(p, k) else e.getOrientation(p)
        val time = if (h) e.getHistoricalEventTime(k) else e.eventTime
        val (tx, ty) = tiltXY(tilt, orientation)

        return PenSample(
            x = (x / width).coerceIn(0f, 1f),
            y = (y / height).coerceIn(0f, 1f),
            pressure = if (inContact) pressure.coerceIn(0f, 1f) else 0f,
            tiltX = tx,
            tiltY = ty,
            inRange = true,
            inContact = inContact,
            barrel = (e.buttonState and MotionEvent.BUTTON_STYLUS_PRIMARY) != 0,
            eraser = e.getToolType(p) == MotionEvent.TOOL_TYPE_ERASER,
            timeMs = time,
        )
    }

    /**
     * Android gives tilt as "angle away from straight up" + "which direction it leans".
     * Windows wants "lean left/right" (tiltX) and "lean toward/away from you" (tiltY).
     * NOTE: the signs may need flipping. Check in Krita during Milestone 4.
     */
    private fun tiltXY(tilt: Float, orientation: Float): Pair<Int, Int> {
        val t = tan(tilt.coerceIn(0f, 1.55f)) // 1.55 rad ≈ 89°, avoids infinity
        val tx = Math.toDegrees(atan(t * sin(orientation)).toDouble()).roundToInt()
        val ty = Math.toDegrees(atan(-t * cos(orientation)).toDouble()).roundToInt()
        return tx to ty
    }

    private fun emit(s: PenSample) {
        last = s
        onSample(s)
        invalidate() // redraw the debug info
    }

    // ---- Debug display: status, numbers, and a dot under the pen -----------
    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        canvas.drawText(status, 40f, 80f, textPaint)
        val s = last ?: return
        val info = "x=%.3f  y=%.3f  pressure=%.3f  tilt=%d,%d  %s%s".format(
            s.x, s.y, s.pressure, s.tiltX, s.tiltY,
            if (s.inContact) "TOUCH" else if (s.inRange) "hover" else "away",
            if (s.barrel) "  [button]" else "",
        )
        canvas.drawText(info, 40f, 140f, textPaint)
        if (s.inRange) canvas.drawCircle(s.x * width, s.y * height, 6f + 30f * s.pressure, dotPaint)
    }
}
```

### 5.5 `PenLink.kt`: the connection

Runs on its own thread so the network **never** slows down drawing. It:
1. Connects to `127.0.0.1:47800` (on the phone itself; `adb reverse` carries it to the PC).
2. Sends HELLO and waits for HELLO_ACK.
3. Takes packets from the queue and writes them. It sends everything that's waiting in one go, then `flush()`es.
4. If nothing happens for 0.5 s, it sends a PING.
5. If anything fails, it waits 1 s and starts over, forever.

```kotlin
package com.spentablet.pad

import java.io.BufferedOutputStream
import java.io.DataInputStream
import java.net.InetSocketAddress
import java.net.Socket
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit

class PenLink(
    private val host: String,
    private val port: Int,
    private val padSize: () -> Pair<Int, Int>,
    private val onStatus: (String) -> Unit,
) {
    private val queue = LinkedBlockingQueue<ByteArray>(4096)
    @Volatile private var running = false
    @Volatile private var socket: Socket? = null
    @Volatile private var connected = false
    private var thread: Thread? = null
    private var seq = 0

    /** Called from the UI thread for every pen sample. Never blocks. */
    fun send(s: PenSample) {
        if (connected) queue.offer(Protocol.pen(s, seq++))
    }

    fun start() {
        if (running) return
        running = true
        thread = Thread(::loop, "PenLink").apply { priority = Thread.MAX_PRIORITY; start() }
    }

    fun stop() {
        running = false
        connected = false
        thread?.interrupt()
        runCatching { socket?.close() }
    }

    private fun loop() {
        while (running) {
            try {
                onStatus("Connecting to PC…")
                Socket().use { sock ->
                    socket = sock
                    sock.tcpNoDelay = true // send small packets immediately
                    sock.connect(InetSocketAddress(host, port), 1000)

                    // Wait until the view has been laid out so we know its size.
                    var (w, h) = padSize()
                    while (running && (w == 0 || h == 0)) { Thread.sleep(50); padSize().let { w = it.first; h = it.second } }

                    val out = BufferedOutputStream(sock.getOutputStream(), 8192)
                    out.write(Protocol.hello(w, h)); out.flush()

                    // Wait for the PC to answer (see Gotchas: adb reverse quirk).
                    sock.soTimeout = 2000
                    val ack = ByteArray(Protocol.SIZE)
                    DataInputStream(sock.getInputStream()).readFully(ack)
                    check(ack[0] == Protocol.T_HELLO_ACK) { "PC sent an unexpected reply" }

                    queue.clear()
                    connected = true
                    onStatus("Connected ✓   (${w}×${h})")

                    while (running) {
                        val first = queue.poll(500, TimeUnit.MILLISECONDS) ?: Protocol.ping()
                        out.write(first)
                        while (true) out.write(queue.poll() ?: break) // drain everything waiting
                        out.flush()
                    }
                }
            } catch (e: Exception) {
                connected = false
                if (!running) break
                onStatus("Not connected: is the PC app running and the cable plugged in?")
                try { Thread.sleep(1000) } catch (_: InterruptedException) { break }
            }
        }
        connected = false
    }
}
```

### 5.6 `MainActivity.kt`: putting it together

```kotlin
package com.spentablet.pad

import android.os.Bundle
import android.view.WindowManager
import androidx.activity.ComponentActivity
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat

class MainActivity : ComponentActivity() {
    private lateinit var pad: PadView
    private lateinit var link: PenLink

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON) // don't dim while drawing

        pad = PadView(this) { sample -> link.send(sample) }
        link = PenLink(
            host = "127.0.0.1",
            port = Protocol.PORT,
            padSize = { pad.width to pad.height },
            onStatus = { text -> runOnUiThread { pad.status = text } },
        )
        setContentView(pad)

        // Full screen: hide status bar + navigation bar. Swipe from an edge to show them briefly.
        WindowCompat.setDecorFitsSystemWindows(window, false)
        WindowInsetsControllerCompat(window, pad).apply {
            hide(WindowInsetsCompat.Type.systemBars())
            systemBarsBehavior = WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
        }
    }

    override fun onStart() { super.onStart(); link.start() }
    override fun onStop() { link.stop(); super.onStop() }
}
```

**To run it:** with the phone plugged in, select it in Android Studio's device dropdown and press the green ▶ Run button.

---

## 6. Windows app

### 6.1 `SPenTablet.Host.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <!-- Our Windows API structs are laid out for 64-bit. -->
    <PlatformTarget>x64</PlatformTarget>
    <!-- Use REAL pixels on scaled displays (125%, 150%...). Without this the pen lands in the wrong place. -->
    <ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>
  </PropertyGroup>
</Project>
```

### 6.2 `Input/NativeMethods.cs`: talking to Windows

These are C# copies of structures from Windows' `winuser.h`. The field order and sizes **must** match Windows exactly, so copy carefully.

```csharp
using System.Runtime.InteropServices;

namespace SPenTablet.Host.Input;

internal static class NativeMethods
{
    public const uint PT_PEN = 3;
    public const int POINTER_FEEDBACK_DEFAULT = 1; // 3 = NONE (no ripple/visual feedback)

    [Flags]
    public enum PointerFlags : uint
    {
        None = 0,
        New = 0x1,
        InRange = 0x2,
        InContact = 0x4,
        FirstButton = 0x10,
        SecondButton = 0x20,
        Primary = 0x2000,
        Confidence = 0x4000,
        Canceled = 0x8000,
        Down = 0x10000,
        Update = 0x20000,
        Up = 0x40000,
    }

    [Flags] public enum PenFlags : uint { None = 0, Barrel = 0x1, Inverted = 0x2, Eraser = 0x4 }
    [Flags] public enum PenMask : uint { None = 0, Pressure = 0x1, Rotation = 0x2, TiltX = 0x4, TiltY = 0x8 }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINTER_INFO
    {
        public uint pointerType;
        public uint pointerId;
        public uint frameId;
        public PointerFlags pointerFlags;
        public IntPtr sourceDevice;
        public IntPtr hwndTarget;
        public POINT ptPixelLocation;
        public POINT ptHimetricLocation;
        public POINT ptPixelLocationRaw;
        public POINT ptHimetricLocationRaw;
        public uint dwTime;
        public uint historyCount;
        public int inputData;
        public uint dwKeyStates;
        public ulong PerformanceCount;
        public int ButtonChangeType;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINTER_PEN_INFO
    {
        public POINTER_INFO pointerInfo;
        public PenFlags penFlags;
        public PenMask penMask;
        public uint pressure;   // 0..1024
        public uint rotation;   // 0..359
        public int tiltX;       // -90..90
        public int tiltY;       // -90..90
    }

    // In C this is { type; union { touchInfo; penInfo; } }. The union is 8-byte aligned,
    // and its size is that of the larger touch struct, so the whole thing is 152 bytes on x64.
    [StructLayout(LayoutKind.Explicit, Size = 152)]
    public struct POINTER_TYPE_INFO
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public POINTER_PEN_INFO penInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr CreateSyntheticPointerDevice(uint pointerType, uint maxCount, int mode);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool InjectSyntheticPointerInput(IntPtr device, [In] POINTER_TYPE_INFO[] pointerInfo, uint count);

    [DllImport("user32.dll")]
    public static extern void DestroySyntheticPointerDevice(IntPtr device);
}
```

### 6.3 `Input/PenInjector.cs`: the pen state machine

Windows expects pen events in a strict order. Think of the pen as being in one of three states:

```
           hover in                 tip touches
  AWAY ─────────────► HOVERING ─────────────────► TOUCHING
   ▲   ◄─────────────    ▲     ◄─────────────────     │
   │     hover out       │         tip lifts          │
   └─────────────────────┴──── cable unplugged ───────┘   (ReleaseAll: lift, then leave)
```

| Transition | Flags we send |
|---|---|
| hovering (enter or move) | `InRange \| Update` |
| tip touches | `InRange \| InContact \| FirstButton \| Down` |
| moving while touching | `InRange \| InContact \| FirstButton \| Update` |
| tip lifts | `InRange \| Up` |
| pen leaves | `Update` (no `InRange`) |

**Important:** if the connection drops while the pen is touching, we **must** send "lift" ourselves. Otherwise Windows thinks the pen is stuck down forever and your mouse stops working properly. That's what `ReleaseAll()` is for.

```csharp
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using static SPenTablet.Host.Input.NativeMethods;

namespace SPenTablet.Host.Input;

/// <summary>A virtual pen. Create and use it on ONE thread only.</summary>
public sealed class PenInjector : IDisposable
{
    private readonly IntPtr _device;
    private readonly POINTER_TYPE_INFO[] _info = new POINTER_TYPE_INFO[1];
    private bool _inRange, _inContact;
    private int _lastX, _lastY;

    public PenInjector()
    {
        _device = CreateSyntheticPointerDevice(PT_PEN, 1, POINTER_FEEDBACK_DEFAULT);
        if (_device == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Update(int x, int y, bool inRange, bool inContact, uint pressure,
                       int tiltX, int tiltY, bool barrel, bool eraser)
    {
        if (!inRange) { ReleaseAll(); return; }

        PointerFlags flags;
        if (inContact && !_inContact)      flags = PointerFlags.InRange | PointerFlags.InContact | PointerFlags.FirstButton | PointerFlags.Down;
        else if (!inContact && _inContact) flags = PointerFlags.InRange | PointerFlags.Up;
        else if (inContact)                flags = PointerFlags.InRange | PointerFlags.InContact | PointerFlags.FirstButton | PointerFlags.Update;
        else                               flags = PointerFlags.InRange | PointerFlags.Update;

        Send(x, y, flags, inContact ? pressure : 0, tiltX, tiltY, barrel, eraser, inContact);
        _inRange = true;
        _inContact = inContact;
        _lastX = x;
        _lastY = y;
    }

    /// <summary>Lift the pen (if down) and take it out of range. Safe to call any time.</summary>
    public void ReleaseAll()
    {
        if (_inContact) Send(_lastX, _lastY, PointerFlags.InRange | PointerFlags.Up, 0, 0, 0, false, false, false);
        if (_inRange || _inContact) Send(_lastX, _lastY, PointerFlags.Update, 0, 0, 0, false, false, false);
        _inRange = _inContact = false;
    }

    private void Send(int x, int y, PointerFlags flags, uint pressure, int tiltX, int tiltY,
                      bool barrel, bool eraser, bool inContact)
    {
        ref var t = ref _info[0];
        t = default;
        t.type = PT_PEN;

        ref var pen = ref t.penInfo;
        pen.pointerInfo.pointerType = PT_PEN;
        pen.pointerInfo.pointerId = 0;
        pen.pointerInfo.pointerFlags = flags;
        pen.pointerInfo.ptPixelLocation = new POINT { X = x, Y = y };

        var pf = PenFlags.None;
        if (barrel) pf |= PenFlags.Barrel;
        if (eraser) pf |= PenFlags.Inverted;              // eraser end near the screen
        if (eraser && inContact) pf |= PenFlags.Eraser;   // eraser end touching
        pen.penFlags = pf;

        pen.penMask = PenMask.Pressure | PenMask.TiltX | PenMask.TiltY;
        pen.pressure = pressure;
        pen.tiltX = Math.Clamp(tiltX, -90, 90);
        pen.tiltY = Math.Clamp(tiltY, -90, 90);

        if (!InjectSyntheticPointerInput(_device, _info, 1))
            Debug.WriteLine($"InjectSyntheticPointerInput failed: error {Marshal.GetLastWin32Error()}");
    }

    public void Dispose()
    {
        ReleaseAll();
        DestroySyntheticPointerDevice(_device);
    }
}
```

### 6.4 `Net/Protocol.cs`: decoding (mirror of `Protocol.kt`)

```csharp
using System.Buffers.Binary;

namespace SPenTablet.Host.Net;

public static class Protocol
{
    public const int Port = 47800;
    public const int Size = 24;
    public const uint Magic = 0x42545053; // "SPTB"

    public const byte THello = 0x01, TPen = 0x02, TPing = 0x04, THelloAck = 0x81;

    [Flags] public enum PenBits : byte { InRange = 1, Contact = 2, Barrel = 4, Eraser = 8 }

    public readonly record struct Hello(int Version, int Width, int Height);

    public readonly record struct Pen(PenBits Flags, ushort Seq, float X, float Y, float Pressure,
                                      sbyte TiltX, sbyte TiltY, uint TimeMs)
    {
        public bool InRange => Flags.HasFlag(PenBits.InRange);
        public bool InContact => Flags.HasFlag(PenBits.Contact);
        public bool Barrel => Flags.HasFlag(PenBits.Barrel);
        public bool Eraser => Flags.HasFlag(PenBits.Eraser);
    }

    public static Hello ParseHello(ReadOnlySpan<byte> b)
    {
        if (b[0] != THello || BinaryPrimitives.ReadUInt32LittleEndian(b[4..]) != Magic)
            throw new InvalidDataException("Not the S Pen pad app");
        return new Hello(b[1],
            BinaryPrimitives.ReadInt32LittleEndian(b[8..]),
            BinaryPrimitives.ReadInt32LittleEndian(b[12..]));
    }

    public static Pen ParsePen(ReadOnlySpan<byte> b) => new(
        (PenBits)b[1],
        BinaryPrimitives.ReadUInt16LittleEndian(b[2..]),
        BinaryPrimitives.ReadSingleLittleEndian(b[4..]),
        BinaryPrimitives.ReadSingleLittleEndian(b[8..]),
        BinaryPrimitives.ReadSingleLittleEndian(b[12..]),
        (sbyte)b[16],
        (sbyte)b[17],
        BinaryPrimitives.ReadUInt32LittleEndian(b[20..]));

    public static byte[] HelloAck()
    {
        var a = new byte[Size];
        a[0] = THelloAck;
        a[1] = 1;
        return a;
    }
}
```

### 6.5 `Mapping/AreaMapper.cs`: phone → monitor

The phone screen is wider (about 19.5:9) than most monitors (16:9). If we stretched the whole phone onto the monitor, circles would come out as ovals. With **Keep aspect** on, we use a centered part of the phone that has the same shape as the monitor:

```
 phone (wide):  ┌──┬──────────────────────┬──┐
                │  │   active area 16:9   │  │   ← the edges are unused
                └──┴──────────────────────┴──┘
```

```csharp
using System.Drawing;

namespace SPenTablet.Host.Mapping;

public sealed class AreaMapper
{
    private sealed record State(Rectangle Monitor, float Ax, float Ay, float Aw, float Ah);

    private readonly object _lock = new();
    private volatile State _state;
    private Rectangle _monitor;
    private int _padW = 19, _padH = 9;
    private bool _keepAspect;

    public AreaMapper(Rectangle monitor, bool keepAspect)
    {
        _monitor = monitor;
        _keepAspect = keepAspect;
        _state = Compute();
    }

    public void SetMonitor(Rectangle m)    { lock (_lock) { _monitor = m; _state = Compute(); } }
    public void SetKeepAspect(bool keep)   { lock (_lock) { _keepAspect = keep; _state = Compute(); } }
    public void SetPad(int w, int h)
    {
        lock (_lock)
        {
            if (w > 0 && h > 0) { _padW = w; _padH = h; }
            _state = Compute();
        }
    }

    /// <summary>The active area of the phone, as fractions (0..1) of the pad.</summary>
    public RectangleF ActiveArea { get { var s = _state; return new(s.Ax, s.Ay, s.Aw, s.Ah); } }

    private State Compute()
    {
        float ax = 0, ay = 0, aw = 1, ah = 1;
        if (_keepAspect)
        {
            float pad = _padW / (float)_padH;
            float mon = _monitor.Width / (float)_monitor.Height;
            if (pad > mon) { aw = mon / pad; ax = (1 - aw) / 2; } // phone wider → trim left/right
            else           { ah = pad / mon; ay = (1 - ah) / 2; } // phone taller → trim top/bottom
        }
        return new State(_monitor, ax, ay, aw, ah);
    }

    /// <summary>Convert phone coords (0..1) to screen pixels.</summary>
    public (int X, int Y) Map(float nx, float ny)
    {
        var s = _state; // one consistent snapshot, even if settings change mid-stroke
        float u = Math.Clamp((nx - s.Ax) / s.Aw, 0f, 1f);
        float v = Math.Clamp((ny - s.Ay) / s.Ah, 0f, 1f);
        return (s.Monitor.Left + (int)MathF.Round(u * (s.Monitor.Width - 1)),
                s.Monitor.Top  + (int)MathF.Round(v * (s.Monitor.Height - 1)));
    }
}
```

### 6.6 `Mapping/PressureCurve.cs`

- **MinPressure:** pressure below this counts as zero, so the faint "just resting" touch doesn't leave marks.
- **Gamma:** below 1 = **soft** (light touch gives more ink). Above 1 = **firm** (you have to press harder).

```
 output│        firm (1.6) is the lowest curve,
       │        soft (0.6) is the highest
   1.0 ┤                    ..::
       │              ..:::''
       │         .:''   ...''
       │      .:'  ..''  ..'
       │    .'  .''   .''
       │  .' .''   .''
       │ :.'   ..''
   0.0 ┼'''''''───────────── input
       0                    1.0
```

```csharp
namespace SPenTablet.Host.Mapping;

public sealed record PressureCurve(float Gamma = 1.0f, float MinPressure = 0.02f)
{
    public static readonly PressureCurve Soft = new(0.6f);
    public static readonly PressureCurve Normal = new(1.0f);
    public static readonly PressureCurve Firm = new(1.6f);

    /// <summary>Phone pressure 0..1 → Windows pressure 1..1024 (only called while touching).</summary>
    public uint ToWindows(float p)
    {
        float t = Math.Clamp((p - MinPressure) / (1f - MinPressure), 0f, 1f);
        float v = MathF.Pow(t, Gamma);
        return (uint)Math.Clamp(MathF.Round(v * 1024f), 1f, 1024f); // never 0 while touching
    }
}
```

### 6.7 `Net/PenServer.cs`: the receiving thread

It uses **one dedicated thread** with plain blocking reads, rather than async code. That keeps the delay as low as possible and guarantees the virtual pen is always used from the same thread.

```csharp
using System.Net;
using System.Net.Sockets;
using SPenTablet.Host.Input;
using SPenTablet.Host.Mapping;

namespace SPenTablet.Host.Net;

public sealed class PenServer
{
    private readonly AreaMapper _mapper;
    private readonly Func<PressureCurve> _curve;
    private TcpListener? _listener;
    private volatile bool _running;

    public event Action<string>? Status;
    public bool IsConnected { get; private set; }

    public PenServer(AreaMapper mapper, Func<PressureCurve> curve)
    {
        _mapper = mapper;
        _curve = curve;
    }

    public void Start()
    {
        _running = true;
        new Thread(Run) { IsBackground = true, Name = "PenServer", Priority = ThreadPriority.Highest }.Start();
    }

    public void Stop()
    {
        _running = false;
        _listener?.Stop();
    }

    private void Run()
    {
        using var pen = new PenInjector();
        // Loopback only: nothing on your network can connect. adb reverse arrives as loopback.
        _listener = new TcpListener(IPAddress.Loopback, Protocol.Port);
        _listener.Start();
        var buf = new byte[Protocol.Size];

        while (_running)
        {
            Status?.Invoke("Waiting for phone…");
            TcpClient client;
            try { client = _listener.AcceptTcpClient(); }
            catch (SocketException) { break; } // listener stopped

            try { HandleClient(client, pen, buf); }
            catch (Exception ex) when (ex is IOException or SocketException or InvalidDataException) { }
            finally
            {
                pen.ReleaseAll(); // never leave the pen stuck down
                IsConnected = false;
                client.Dispose();
            }
        }
    }

    private void HandleClient(TcpClient client, PenInjector pen, byte[] buf)
    {
        client.NoDelay = true;
        client.ReceiveTimeout = 3000; // phone pings every 0.5 s; 3 s of silence = cable gone
        var stream = client.GetStream();

        stream.ReadExactly(buf);
        var hello = Protocol.ParseHello(buf);
        _mapper.SetPad(hello.Width, hello.Height);
        stream.Write(Protocol.HelloAck());

        IsConnected = true;
        Status?.Invoke($"Connected ✓ (pad {hello.Width}×{hello.Height})");

        while (_running)
        {
            stream.ReadExactly(buf);
            if (buf[0] != Protocol.TPen) continue; // ping or unknown: ignore

            var p = Protocol.ParsePen(buf);
            var (x, y) = _mapper.Map(p.X, p.Y);
            uint pressure = p.InContact ? _curve().ToWindows(p.Pressure) : 0;
            pen.Update(x, y, p.InRange, p.InContact, pressure, p.TiltX, p.TiltY, p.Barrel, p.Eraser);
        }
    }
}
```

### 6.8 `Adb/AdbHelper.cs`

`adb reverse tcp:47800 tcp:47800` means "when an app on the phone connects to its own port 47800, tunnel it through USB to the PC's port 47800." The tunnel disappears whenever the cable is unplugged, so we simply run the command again every few seconds while no phone is connected. Running it again is harmless.

```csharp
using System.Diagnostics;

namespace SPenTablet.Host.Adb;

public static class AdbHelper
{
    public static string? Find(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath)) return configuredPath;

        var sdk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                               "Android", "Sdk", "platform-tools", "adb.exe");
        if (File.Exists(sdk)) return sdk;

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), "adb.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { } // ignore malformed PATH entries
        }
        return null;
    }

    /// <summary>Runs "adb reverse". Returns false if adb is missing or no phone is attached.</summary>
    public static bool TryReverse(string? configuredPath, int port)
    {
        var adb = Find(configuredPath);
        if (adb is null) return false;

        var psi = new ProcessStartInfo(adb, $"reverse tcp:{port} tcp:{port}")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var proc = Process.Start(psi);
        if (proc is null) return false;
        if (!proc.WaitForExit(5000)) { proc.Kill(); return false; }
        return proc.ExitCode == 0;
    }
}
```

### 6.9 `AppSettings.cs`

Settings are saved to `%APPDATA%\SPenTablet\settings.json`.

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using SPenTablet.Host.Mapping;

namespace SPenTablet.Host;

public sealed class AppSettings
{
    public int MonitorIndex { get; set; }
    public float Gamma { get; set; } = 1.0f;
    public float MinPressure { get; set; } = 0.02f;
    public bool KeepAspect { get; set; } = true;
    public string? AdbPath { get; set; }

    [JsonIgnore] public PressureCurve Curve => new(Gamma, MinPressure);

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SPenTablet", "settings.json");

    public static AppSettings Load()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); } // missing or corrupt file: use defaults
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
```

### 6.10 `TrayApp.cs` and `Program.cs`

The PC app has no window. It lives as an icon in the system tray (bottom-right). Right-click the icon for: status, choose monitor, pressure feel, keep aspect, exit.

```csharp
using SPenTablet.Host.Adb;
using SPenTablet.Host.Mapping;
using SPenTablet.Host.Net;

namespace SPenTablet.Host;

internal sealed class TrayApp : ApplicationContext
{
    private readonly SynchronizationContext _ui = new WindowsFormsSynchronizationContext();
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _status = new("Starting…") { Enabled = false };
    private readonly AreaMapper _mapper;
    private readonly PenServer _server;
    private readonly System.Windows.Forms.Timer _adbTimer = new() { Interval = 3000 };

    public TrayApp()
    {
        var screens = Screen.AllScreens;
        var screen = _settings.MonitorIndex < screens.Length ? screens[_settings.MonitorIndex] : Screen.PrimaryScreen!;
        _mapper = new AreaMapper(screen.Bounds, _settings.KeepAspect);

        _server = new PenServer(_mapper, () => _settings.Curve);
        _server.Status += text => _ui.Post(_ => SetStatus(text), null); // hop to the UI thread

        var menu = new ContextMenuStrip();
        menu.Items.Add(_status);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(BuildMonitorMenu());
        menu.Items.Add(BuildPressureMenu());
        var keep = new ToolStripMenuItem("Keep aspect ratio") { Checked = _settings.KeepAspect, CheckOnClick = true };
        keep.CheckedChanged += (_, _) => { _settings.KeepAspect = keep.Checked; _mapper.SetKeepAspect(keep.Checked); _settings.Save(); };
        menu.Items.Add(keep);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application, // replace with your own .ico later
            Text = "S Pen Tablet",
            ContextMenuStrip = menu,
            Visible = true,
        };

        // Keep the USB tunnel alive while no phone is connected (run adb off the UI thread).
        _adbTimer.Tick += (_, _) =>
        {
            if (!_server.IsConnected) Task.Run(() => AdbHelper.TryReverse(_settings.AdbPath, Protocol.Port));
        };
        _adbTimer.Start();
        Task.Run(() => AdbHelper.TryReverse(_settings.AdbPath, Protocol.Port));

        _server.Start();
    }

    private void SetStatus(string text)
    {
        _status.Text = text;
        _icon.Text = ("S Pen Tablet: " + text)[..Math.Min(63, ("S Pen Tablet: " + text).Length)]; // tooltip max 63 chars
    }

    private ToolStripMenuItem BuildMonitorMenu()
    {
        var root = new ToolStripMenuItem("Monitor");
        var screens = Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
        {
            int index = i;
            var s = screens[i];
            var item = new ToolStripMenuItem($"{i + 1}: {s.Bounds.Width}×{s.Bounds.Height}{(s.Primary ? " (main)" : "")}")
            {
                Checked = index == _settings.MonitorIndex,
            };
            item.Click += (_, _) =>
            {
                _settings.MonitorIndex = index;
                _settings.Save();
                _mapper.SetMonitor(s.Bounds);
                foreach (ToolStripMenuItem other in root.DropDownItems) other.Checked = other == item;
            };
            root.DropDownItems.Add(item);
        }
        return root;
    }

    private ToolStripMenuItem BuildPressureMenu()
    {
        var root = new ToolStripMenuItem("Pressure feel");
        foreach (var (name, curve) in new[] { ("Soft", PressureCurve.Soft), ("Normal", PressureCurve.Normal), ("Firm", PressureCurve.Firm) })
        {
            var item = new ToolStripMenuItem(name) { Checked = Math.Abs(_settings.Gamma - curve.Gamma) < 0.01f };
            item.Click += (_, _) =>
            {
                _settings.Gamma = curve.Gamma;
                _settings.Save();
                foreach (ToolStripMenuItem other in root.DropDownItems) other.Checked = other == item;
            };
            root.DropDownItems.Add(item);
        }
        return root;
    }

    protected override void ExitThreadCore()
    {
        _server.Stop();
        _adbTimer.Stop();
        _icon.Visible = false;
        _icon.Dispose();
        base.ExitThreadCore();
    }
}
```

```csharp
namespace SPenTablet.Host;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Only one copy may run (two would fight over port 47800).
        using var mutex = new Mutex(true, "SPenTablet.Host.SingleInstance", out bool isFirst);
        if (!isFirst) return;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp());
    }
}
```

**To run it:**

```bash
dotnet run --project windows/SPenTablet.Host
```

You can also press ▶ in Visual Studio.

---

## 7. Setting up Windows & Krita

### Krita (required)
Settings → Configure Krita → **Tablet settings** → Tablet Input API → **Windows 8+ Pointer Input (Windows Ink, etc.)** → OK → **restart Krita**.

Krita's other option, *WinTab*, only talks to real tablet drivers, so it won't see our virtual pen.

### Windows (recommended)
Settings → Bluetooth & devices → **Pen & Windows Ink**:
- Turn off **"Show visual effects"**, which draws ripples when you tap. You can also change `POINTER_FEEDBACK_DEFAULT` to `3` (NONE) in `NativeMethods.cs`.
- Under **"Press and hold"** / right-click settings, turn off press-and-hold right-click. Otherwise holding the pen still briefly delays strokes.
- If a handwriting panel pops up when you tap text boxes, turn off "Use your handwriting to enter text" or the handwriting panel option.

---

## 8. Milestones & how to test each one

Do them in order. Each one should **work** before you start the next.

### Milestone 1: Tools installed
- [ ] Android Studio, .NET 10 SDK, Krita installed
- [ ] `adb devices` shows your phone as `device`
- [ ] Both empty projects build (Android: ▶ Run shows the template app; Windows: `dotnet run` opens an empty window)

### Milestone 2: Pen readings on the phone
Build `PenSample`, `Protocol`, `PadView`, and a `MainActivity` that doesn't start the link yet.
- [ ] Hovering shows a small dot and "hover"
- [ ] Touching shows "TOUCH" and the dot grows with pressure
- [ ] Pressing lightly vs. hard changes `pressure` smoothly from about 0.0 to 1.0
- [ ] Fingers do **nothing**
- [ ] Holding the side button shows `[button]`
- [ ] **Write down:** does `tilt` change when you lean the pen? If it's always `0,0`, the phone doesn't report tilt and we simply ignore it. Everything else still works.

### Milestone 3: Cursor follows the pen
Add `PenLink` on the phone and `Protocol`, `PenServer`, `AreaMapper`, `AdbHelper` on the PC. For this step, temporarily make `PenInjector.Update` just call `Cursor.Position = new Point(x, y)`.
- [ ] The phone says **Connected ✓**
- [ ] Moving the pen moves the PC mouse cursor across the whole monitor
- [ ] Unplug the cable: the phone says "Not connected". Plug it back in: it reconnects by itself within a few seconds.

### Milestone 4: Real pen in Krita ⭐
Swap in the real `PenInjector`.
- [ ] Krita: brush strokes get thicker/darker with pressure
- [ ] Hovering moves Krita's brush outline without painting
- [ ] Fast scribbles look smooth (not jagged polygons)
- [ ] Unplugging mid-stroke does **not** leave the pen stuck down
- [ ] If tilt works on your phone: use a tilt-sensitive brush. If leaning left makes it act like leaning right, flip the sign of `tx` in `tiltXY` (same for `ty`).
- [ ] Side button: in Krita, map it to whatever you like (Settings → Configure Krita → Canvas Input Settings)

**🎉 At this point you have a working drawing tablet.**

### Milestone 5: Comfort
- [ ] Monitor selection and pressure presets in the tray menu (already in the code above)
- [ ] Phone draws an outline of the *active area* (the PC sends `ActiveArea` back in a new packet type)
- [ ] Portrait mode option
- [ ] Your own tray icon (`.ico`)
- [ ] "Start with Windows" option
- [ ] Optional: on-phone shortcut buttons (undo, brush size) using new packet types. The PC turns them into keyboard shortcuts with `SendInput`.

### Delay check (do this any time)
Draw fast circles in Krita and watch how far the line trails behind the pen tip. Over USB it should feel close to a real tablet. If it doesn't, see the Gotchas below.

---

## 9. Gotchas

| Symptom | Cause | Fix |
|---|---|---|
| Phone flashes "Connected" even though the PC app is closed | With `adb reverse`, ADB itself accepts the connection on the phone even if nothing listens on the PC | That's why we wait for **HELLO_ACK** before saying Connected (already in the code) |
| Pen stays "stuck down" after unplugging | Windows never got the "lift" event | `ReleaseAll()` in the server's `finally` + the 3 s read timeout (already in the code) |
| Cursor lands in the wrong place on a 125%/150% display | App isn't DPI-aware, so Windows "virtualizes" coordinates | `PerMonitorV2` in the `.csproj` (already in it) |
| Strokes have a tiny gap or flicker at the start | Android's `HOVER_EXIT` arrives right before `DOWN` | The 80 ms delayed "out of range" (already in `PadView`) |
| No pressure in Krita | Krita is using WinTab | Switch to Windows 8+ Pointer Input and restart Krita |
| Pen does nothing in some apps (Task Manager, installers) | Windows blocks normal apps from injecting input into **admin** windows (UIPI) | Expected. Running the host as admin works around it, but avoid doing that by default. |
| Side button opens Samsung's Air command menu | Samsung feature | Turn off Air command in S Pen settings |
| Swiping near the phone's edge triggers Back/Home | System gestures | Immersive mode (already in the code) makes it a 2-step swipe. If it's still annoying, use `View.setSystemGestureExclusionRects` |
| Strokes look jagged / polygon-like | Only sending one sample per event | Send historical samples (already in the code) |
| Laggy | Nagle's algorithm waiting to bundle small packets | `tcpNoDelay = true` / `NoDelay = true` on both sides (already in the code) |
| `CreateSyntheticPointerDevice` fails | Windows older than 10 1809, or a 32-bit build | You're on Windows 11; check `PlatformTarget` is `x64` |
| `adb` "unauthorized" | Phone didn't approve the PC | Unplug, replug, accept the prompt on the phone |
| `adb reverse` "more than one device" | An emulator or second phone is attached | Close the emulator (or later: add `-s <serial>` support) |

---

## 10. Later: Wi-Fi, screen mirroring, real driver

### Wi-Fi mode
- PC listens on the local network instead of loopback. Use **UDP** for pen packets: a lost packet just means a slightly older position, so there's no waiting to resend. Keep TCP for HELLO and settings.
- **Pairing is required for security:** anything that can send our packets can draw and click on your PC. The PC shows a QR code containing its IP, port and a random secret, and the phone scans it. Every packet then carries a short code made from that secret (an HMAC), and the PC drops anything that doesn't match.
- Expect a bit more delay and occasional hiccups, depending on your router.

### Screen mirroring ("draw on the image", like a Cintiq)
- **PC:** capture the monitor with **Windows.Graphics.Capture** (or DXGI Desktop Duplication). Encode it to H.264/HEVC with the GPU (Media Foundation). Stream it to the phone.
- **Phone:** decode with **MediaCodec** directly onto a `SurfaceView` placed under the `PadView`.
- The pen part stays exactly the same, with mapping set to "whole pad = whole monitor".
- This is roughly as much work as everything else combined. Do it last.

### A real tablet driver (only if ever needed)
If an app refuses to work with Windows Ink, you'd write a Windows **kernel driver** using the **Virtual HID Framework (VHF)**. It describes a "HID pen digitizer", so Windows treats it as genuine hardware, and you could add **WinTab** support on top. It needs driver signing (test mode while developing). For Krita this is **not needed**.

---

## 11. Glossary

| Term | Meaning |
|---|---|
| **ADB** | Android Debug Bridge: a PC tool that talks to your phone over USB. Comes with Android Studio. |
| **`adb reverse`** | Makes a port on the phone forward to a port on the PC, through the USB cable. |
| **TCP** | A reliable "pipe" between two programs: bytes arrive in order, none lost. |
| **Port** | A number (we use 47800) that identifies which program on a computer receives a connection. |
| **Loopback / 127.0.0.1** | "This same device." Only programs on the same machine can connect. |
| **Packet** | One chunk of bytes with an agreed meaning (ours are always 24 bytes). |
| **Little-endian** | The byte order for numbers: least significant byte first. Both sides must agree. |
| **Normalized coordinates** | Positions as 0.0–1.0 fractions instead of pixels, so any screen size works. |
| **MotionEvent** | Android's object describing a touch/pen event (position, pressure, tool type…). |
| **Historical samples** | Extra pen readings Android packed into one MotionEvent. |
| **Windows Ink / Pointer Input** | Windows' modern pen system. Our virtual pen speaks it. |
| **WinTab** | An older tablet system from Wacom. Some apps prefer it; Krita can use either. |
| **Synthetic pointer device** | A fake pen created by software that Windows treats like a real one. |
| **DPI awareness** | Whether an app uses real pixels on scaled (125%, 150%) displays. |
| **Gamma** | The exponent in the pressure curve; changes how pressure "feels". |
| **Tray app** | A program that lives as a small icon near the clock instead of a window. |
