package ru.aokazantsev.pult.session

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import ru.aokazantsev.pult.protocol.JsonFields
import ru.aokazantsev.pult.protocol.message.FeedEntry
import ru.aokazantsev.pult.vectors.Vectors

class FeedVectorsTest {

    private val cases = Vectors.load("messages").getValue("feed_cases").jsonArray

    @Test
    fun everyCaseStoresAndMarksAsExpected() {
        cases.forEach { case ->
            val obj = case.jsonObject
            val name = JsonFields.string(obj, "name")!!
            val store = FeedStore()
            store.add(THREAD, entries(obj.getValue("stored").jsonArray))
            obj.getValue("incoming").jsonArray.forEach { batch -> store.add(THREAD, entries(batch.jsonArray)) }
            val slice = store.slice(THREAD, JsonFields.integer(obj["last_seq"]))
            assertEquals(obj.getValue("expected_stored").jsonArray.map { (it as JsonPrimitive).content.toLong() }, slice.entries.map { it.seq }, name)
            assertEquals(obj.getValue("markers"), markersOf(slice.markers), name)
        }
    }

    private fun entries(seqs: JsonArray): List<FeedEntry> =
        seqs.map { FeedEntry.Notice(seq = (it as JsonPrimitive).content.toLong(), at = null, truncated = false, text = "n") }

    private fun markersOf(markers: List<FeedMarker>): JsonArray = JsonArray(
        markers.map { marker ->
            when (marker) {
                FeedMarker.Start -> JsonObject(mapOf("where" to JsonPrimitive("start")))
                FeedMarker.End -> JsonObject(mapOf("where" to JsonPrimitive("end")))
                is FeedMarker.Between -> JsonObject(
                    mapOf(
                        "where" to JsonPrimitive("between"),
                        "after" to JsonPrimitive(marker.after),
                        "missing" to JsonPrimitive(marker.missing),
                    ),
                )
            }
        },
    )

    private companion object {
        const val THREAD = "t1"
    }
}
