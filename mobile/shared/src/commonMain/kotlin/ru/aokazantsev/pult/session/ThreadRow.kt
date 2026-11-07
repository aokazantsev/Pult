package ru.aokazantsev.pult.session

import ru.aokazantsev.pult.protocol.message.ThreadInfo

data class ThreadRow(val info: ThreadInfo, val depth: Int, val marks: ThreadMarks)
