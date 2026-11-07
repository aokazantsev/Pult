package ru.aokazantsev.pult.protocol.message

data class ThreadInfo(
    val thread: String,
    val title: String,
    val folder: String,
    val status: ThreadStatus,
    val pcDialog: Boolean,
    val error: String? = null,
    val parent: String? = null,
    val forkOf: ForkOf? = null,
    val turnNote: TurnNote? = null,
    val lastSeq: Long? = null,
)
