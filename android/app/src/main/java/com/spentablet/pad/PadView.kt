package com.spentablet.pad

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Rect
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

    // Ask Android not to take edge swipes anywhere on the pad, so pen input reaches every edge.
    // (Android caps how much of the screen can be excluded, so the system may trim this.)
    override fun onLayout(changed: Boolean, l: Int, t: Int, r: Int, b: Int) {
        super.onLayout(changed, l, t, r, b)
        systemGestureExclusionRects = listOf(Rect(0, 0, r - l, b - t))
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
        val tx = Math.toDegrees(atan(-t * sin(orientation)).toDouble()).roundToInt()
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
