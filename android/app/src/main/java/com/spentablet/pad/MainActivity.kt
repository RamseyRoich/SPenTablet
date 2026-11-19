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
