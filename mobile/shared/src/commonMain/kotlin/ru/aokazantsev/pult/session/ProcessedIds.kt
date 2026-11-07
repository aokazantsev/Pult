package ru.aokazantsev.pult.session

class ProcessedIds(private val limit: Int = LIMIT) {

    private val ids = LinkedHashSet<String>()

    fun firstTime(id: String): Boolean {
        if (!ids.add(id)) return false
        if (ids.size > limit) ids.remove(ids.first())
        return true
    }

    fun clear() {
        ids.clear()
    }

    private companion object {
        const val LIMIT = 2000
    }
}
