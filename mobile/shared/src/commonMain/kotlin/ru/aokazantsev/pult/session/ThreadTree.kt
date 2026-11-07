package ru.aokazantsev.pult.session

import ru.aokazantsev.pult.protocol.message.ThreadInfo

object ThreadTree {

    private const val MAX_DEPTH = 8

    fun build(threads: List<ThreadInfo>, cards: List<RequestCard>): List<ThreadRow> {
        val known = threads.associateBy { it.thread }
        val own = ownMarks(cards)
        val children = threads.groupBy { info -> info.parent?.takeIf { it in known && it != info.thread } }
        val rows = ArrayList<ThreadRow>()
        val visited = HashSet<String>()
        fun visit(info: ThreadInfo, depth: Int) {
            if (!visited.add(info.thread)) return
            rows.add(ThreadRow(info, depth, marksOf(info, children, own, HashSet())))
            children[info.thread].orEmpty().forEach { visit(it, minOf(depth + 1, MAX_DEPTH)) }
        }
        children[null].orEmpty().forEach { visit(it, 0) }
        threads.forEach { visit(it, 0) }
        return rows
    }

    private fun ownMarks(cards: List<RequestCard>): Map<String, ThreadMarks> =
        cards.groupBy { it.thread }.mapValues { (_, threadCards) ->
            ThreadMarks(
                awaitsAction = threadCards.any { it.awaitsAction },
                awaitsText = threadCards.any { it.awaitsText },
            )
        }

    private fun marksOf(
        info: ThreadInfo,
        children: Map<String?, List<ThreadInfo>>,
        own: Map<String, ThreadMarks>,
        path: MutableSet<String>,
    ): ThreadMarks {
        val self = own[info.thread] ?: ThreadMarks()
        if (!path.add(info.thread)) return self
        var childAction = false
        var childText = false
        for (child in children[info.thread].orEmpty()) {
            val marks = marksOf(child, children, own, path)
            childAction = childAction || marks.awaitsAction || marks.childAwaitsAction
            childText = childText || marks.awaitsText || marks.childAwaitsText
        }
        path.remove(info.thread)
        return self.copy(childAwaitsAction = childAction, childAwaitsText = childText)
    }
}
