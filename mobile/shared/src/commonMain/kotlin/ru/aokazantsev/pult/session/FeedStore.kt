package ru.aokazantsev.pult.session

import ru.aokazantsev.pult.protocol.message.FeedEntry

class FeedStore(private val keepPerThread: Int = KEEP_PER_THREAD) {

    private val stored = HashMap<String, HashMap<Long, FeedEntry>>()
    private val cache = HashMap<String, CachedSlice>()

    fun add(thread: String, entries: List<FeedEntry>) {
        val bucket = stored.getOrPut(thread) { HashMap() }
        entries.forEach { entry -> bucket.getOrPut(entry.seq) { entry } }
        if (bucket.size > keepPerThread) {
            bucket.keys.sorted().take(bucket.size - keepPerThread).forEach(bucket::remove)
        }
        cache.remove(thread)
    }

    fun retainOnly(threads: Set<String>) {
        stored.keys.retainAll(threads)
        cache.keys.retainAll(threads)
    }

    fun clear() {
        stored.clear()
        cache.clear()
    }

    fun slice(thread: String, lastSeq: Long?): FeedSlice {
        val cached = cache[thread]
        if (cached != null && cached.lastSeq == lastSeq) return cached.slice
        val entries = stored[thread]?.values?.sortedBy { it.seq }.orEmpty()
        val slice = FeedSlice(entries, markers(entries.map { it.seq }, lastSeq))
        cache[thread] = CachedSlice(lastSeq, slice)
        return slice
    }

    fun position(thread: String): Long? {
        val bucket = stored[thread]?.takeIf { it.isNotEmpty() } ?: return null
        val sorted = bucket.keys.sorted()
        var contiguous = sorted.first()
        for (seq in sorted.drop(1)) {
            if (seq != contiguous + 1) break
            contiguous = seq
        }
        val newest = sorted.last()
        return if (newest - contiguous > TAIL_LIMIT) newest else contiguous
    }

    private fun markers(seqs: List<Long>, lastSeq: Long?): List<FeedMarker> {
        val markers = ArrayList<FeedMarker>()
        if (seqs.isNotEmpty() && seqs.first() > 1) markers.add(FeedMarker.Start)
        seqs.zipWithNext { before, after ->
            if (after - before > 1) markers.add(FeedMarker.Between(after = before, missing = after - before - 1))
        }
        if (lastSeq != null && lastSeq > (seqs.lastOrNull() ?: 0L)) markers.add(FeedMarker.End)
        return markers
    }

    private class CachedSlice(val lastSeq: Long?, val slice: FeedSlice)

    companion object {
        const val KEEP_PER_THREAD = 500
        const val TAIL_LIMIT = 200L
    }
}
