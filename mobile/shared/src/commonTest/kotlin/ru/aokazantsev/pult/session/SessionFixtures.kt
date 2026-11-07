package ru.aokazantsev.pult.session

import ru.aokazantsev.pult.protocol.message.FeedEntry
import ru.aokazantsev.pult.protocol.message.PcMessage
import ru.aokazantsev.pult.protocol.message.PultMode
import ru.aokazantsev.pult.protocol.message.Request
import ru.aokazantsev.pult.protocol.message.ThreadInfo
import ru.aokazantsev.pult.protocol.message.ThreadStatus

object SessionFixtures {

    fun thread(
        id: String,
        parent: String? = null,
        lastSeq: Long? = null,
        status: ThreadStatus = ThreadStatus.Working,
    ) = ThreadInfo(
        thread = id,
        title = "title $id",
        folder = "Pult",
        status = status,
        pcDialog = false,
        parent = parent,
        lastSeq = lastSeq,
    )

    fun permission(id: String, thread: String = "t1", afterSeq: Long = 0) = Request.Permission(
        requestId = id,
        thread = thread,
        title = "title $thread",
        afterSeq = afterSeq,
        tool = "Bash",
        summary = "ls",
        input = emptyList(),
    )

    fun text(id: String, thread: String = "t1", afterSeq: Long = 0) = Request.Text(
        requestId = id,
        thread = thread,
        title = "title $thread",
        afterSeq = afterSeq,
        lastMessage = "done?",
    )

    fun notice(seq: Long) = FeedEntry.Notice(seq = seq, at = null, truncated = false, text = "n$seq")

    fun snapshot(
        id: String = "s1",
        part: Long = 1,
        last: Boolean = true,
        mode: PultMode? = PultMode.Phone,
        threads: List<ThreadInfo> = emptyList(),
        requests: List<Request> = emptyList(),
    ) = PcMessage.Snapshot(
        snapshot = id,
        part = part,
        last = last,
        mode = mode,
        threads = threads,
        requests = requests,
        outcomes = emptyList(),
    )
}
