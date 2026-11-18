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
