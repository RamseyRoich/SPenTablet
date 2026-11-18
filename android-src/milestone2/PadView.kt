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

/**
 * Milestone 2: reads the S Pen and shows the numbers on screen.
 * Nothing is sent anywhere yet (onSample is optional).
 */
class PadView(
    context: Context,
    private val onSample: ((PenSample) -> Unit)? = null,
) : View(context) {

    var status: String = "Milestone 2: pen test (no PC connection yet)"
        set(value) { field = value; invalidate() }

    private var last: PenSample? = null
    private var maxPressure = 0f
    private var sawTilt = false
    private var fingerIgnored = 0

    private val textPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = Color.LTGRAY; textSize = 42f }
    private val dotPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = Color.WHITE }

    private val sendOutOfRange = Runnable {
        val s = last ?: return@Runnable
        emit(s.copy(inRange = false, inContact = false, pressure = 0f))
    }

    init {
        setBackgroundColor(Color.BLACK)
        requestUnbufferedDispatch(InputDevice.SOURCE_CLASS_POINTER)
    }

    // ---- Pen touching the glass -------------------------------------------
    override fun onTouchEvent(e: MotionEvent): Boolean {
        val p = penIndex(e)
        if (p == null) { // finger or palm: ignore, but count it so you can see rejection working
            if (e.actionMasked == MotionEvent.ACTION_DOWN) { fingerIgnored++; invalidate() }
            return true
        }
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
            // HOVER_EXIT fires right before the tip touches down, so wait a moment.
            MotionEvent.ACTION_HOVER_EXIT -> postDelayed(sendOutOfRange, 80)
        }
        return true
    }

    private fun penIndex(e: MotionEvent): Int? =
        (0 until e.pointerCount).firstOrNull {
            val t = e.getToolType(it)
            t == MotionEvent.TOOL_TYPE_STYLUS || t == MotionEvent.TOOL_TYPE_ERASER
        }

    private fun emitAll(e: MotionEvent, p: Int, inContact: Boolean) {
        for (k in 0 until e.historySize) emit(sampleAt(e, p, k, inContact))
        emit(sampleAt(e, p, -1, inContact))
    }

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

    /** Signs may need flipping; we check in Krita at Milestone 4. */
    private fun tiltXY(tilt: Float, orientation: Float): Pair<Int, Int> {
        val t = tan(tilt.coerceIn(0f, 1.55f))
        val tx = Math.toDegrees(atan(t * sin(orientation)).toDouble()).roundToInt()
        val ty = Math.toDegrees(atan(-t * cos(orientation)).toDouble()).roundToInt()
        return tx to ty
    }

    private fun emit(s: PenSample) {
        last = s
        if (s.pressure > maxPressure) maxPressure = s.pressure
        if (s.tiltX != 0 || s.tiltY != 0) sawTilt = true
        onSample?.invoke(s)
        invalidate()
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        canvas.drawText(status, 40f, 80f, textPaint)
        canvas.drawText(
            "max pressure seen: %.3f   tilt ever non-zero: %s   fingers ignored: %d".format(
                maxPressure, if (sawTilt) "YES" else "no", fingerIgnored),
            40f, 140f, textPaint)
        val s = last ?: return
        val state = if (s.inContact) "TOUCH" else if (s.inRange) "hover" else "away"
        canvas.drawText(
            "x=%.3f  y=%.3f  pressure=%.3f  tilt=%d,%d  %s%s%s".format(
                s.x, s.y, s.pressure, s.tiltX, s.tiltY, state,
                if (s.barrel) "  [button]" else "",
                if (s.eraser) "  [eraser]" else ""),
            40f, 200f, textPaint)
        if (s.inRange) canvas.drawCircle(s.x * width, s.y * height, 6f + 30f * s.pressure, dotPaint)
    }
}
