package ru.aokazantsev.pult.session

import ru.aokazantsev.pult.protocol.message.FeedEntry

object TimelineBuilder {

    fun build(slice: FeedSlice, cards: List<RequestCard>): List<TimelineItem> {
        val entries = slice.entries
        val calls = entries.filterIsInstance<FeedEntry.ToolCall>().associateBy { it.call }
        val results = entries.filterIsInstance<FeedEntry.ToolResult>().filter { it.call in calls }.associateBy { it.call }
        val gaps = slice.markers.filterIsInstance<FeedMarker.Between>().associateBy { it.after }
        val pending = cards.sortedBy { it.afterSeq }.toMutableList()
        val items = ArrayList<TimelineItem>()
        if (FeedMarker.Start in slice.markers) items.add(TimelineItem.EarlyMissing)
        var previous: FeedEntry? = null
        for (entry in entries) {
            previous?.let { before ->
                takeCards(pending, items) { it <= before.seq }
                gaps[before.seq]?.let { items.add(TimelineItem.Gap(it.after, it.missing)) }
            }
            takeCards(pending, items) { it < entry.seq }
            previous = entry
            when {
                entry is FeedEntry.ToolResult && entry.call in calls -> Unit
                entry is FeedEntry.ToolCall -> items.add(TimelineItem.ToolStep(entry, results[entry.call]))
                entry is FeedEntry.Unrecognized -> appendUnrecognized(items, entry)
                else -> items.add(TimelineItem.Entry(entry))
            }
        }
        previous?.let { last -> takeCards(pending, items) { it <= last.seq } }
        if (FeedMarker.End in slice.markers) items.add(TimelineItem.LateMissing)
        pending.forEach { items.add(TimelineItem.Card(it)) }
        return items
    }

    private fun takeCards(pending: MutableList<RequestCard>, items: MutableList<TimelineItem>, fits: (Long) -> Boolean) {
        while (pending.isNotEmpty() && fits(pending.first().afterSeq)) items.add(TimelineItem.Card(pending.removeAt(0)))
    }

    private fun appendUnrecognized(items: MutableList<TimelineItem>, entry: FeedEntry.Unrecognized) {
        val last = items.lastOrNull()
        if (last is TimelineItem.Unrecognized) {
            items[items.lastIndex] = last.copy(count = last.count + entry.count)
        } else {
            items.add(TimelineItem.Unrecognized(entry.seq, entry.count))
        }
    }
}
