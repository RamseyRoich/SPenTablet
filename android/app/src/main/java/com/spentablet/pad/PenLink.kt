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
