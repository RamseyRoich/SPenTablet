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
