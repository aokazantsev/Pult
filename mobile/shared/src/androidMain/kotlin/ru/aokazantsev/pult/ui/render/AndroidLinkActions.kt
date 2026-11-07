package ru.aokazantsev.pult.ui.render

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.net.Uri
import ru.aokazantsev.pult.render.LinkTarget

class AndroidLinkActions(private val context: Context) : LinkActions {

    override fun copy(text: String) {
        context.getSystemService(ClipboardManager::class.java)?.setPrimaryClip(ClipData.newPlainText("Pult", text))
    }

    override fun open(url: String) {
        if (!LinkTarget(url).openable) return
        val intent = Intent(Intent.ACTION_VIEW, Uri.parse(url)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        runCatching { context.startActivity(intent) }
    }
}
