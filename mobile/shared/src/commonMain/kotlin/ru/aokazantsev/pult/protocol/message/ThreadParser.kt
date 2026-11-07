package ru.aokazantsev.pult.protocol.message

import kotlinx.serialization.json.JsonElement

internal object ThreadParser {

    fun parse(element: JsonElement, inSnapshot: Boolean): ThreadInfo {
        val fields = FieldReader.item(element, "thread")
        val thread = fields.ident("thread")
        val title = fields.text("title", TITLE_LIMIT, nonEmpty = true)
        val folder = fields.text("folder", TITLE_LIMIT)
        val status = fields.text("status", ENUM_LIMIT)
        val pcDialog = fields.boolean("pc_dialog")
        val error = fields.textOrNull("error", ENUM_LIMIT)
        val parent = fields.identOrNull("parent")
        val forkOf = fields.objectOrNull("fork_of")?.let { fork ->
            ForkOf(title = fork.text("title", TITLE_LIMIT), thread = fork.identOrNull("thread"))
        }
        val turnNote = fields.textOrNull("turn_note", ENUM_LIMIT)
        val lastSeq = if (inSnapshot) fields.integer("last_seq") else fields.integerOrNull("last_seq")
        return ThreadInfo(
            thread = thread,
            title = title,
            folder = folder,
            status = ThreadStatus.fromWire(status),
            pcDialog = pcDialog,
            error = error,
            parent = parent,
            forkOf = forkOf,
            turnNote = TurnNote.fromWire(turnNote),
            lastSeq = lastSeq,
        )
    }

    private const val TITLE_LIMIT = 120
    private const val ENUM_LIMIT = 64
}
