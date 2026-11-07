package ru.aokazantsev.pult.render

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import ru.aokazantsev.pult.protocol.JsonFields
import ru.aokazantsev.pult.vectors.Vectors

class RenderVectorsTest {

    private val cases = Vectors.load("messages").getValue("render").jsonArray

    @Test
    fun sanitizingMatchesTheVectors() {
        cases.forEach { case ->
            val obj = case.jsonObject
            assertEquals(
                JsonFields.string(obj, "sanitized"),
                TextSanitizer.sanitize(JsonFields.string(obj, "input")!!),
                JsonFields.string(obj, "name"),
            )
        }
    }

    @Test
    fun linksMatchTheVectors() {
        cases.forEach { case ->
            val obj = case.jsonObject
            val markdown = JsonFields.string(obj, "field") == "markdown"
            val actual = MarkdownLinks.of(TextSanitizer.sanitize(JsonFields.string(obj, "input")!!), markdown)
            val expected = obj.getValue("links").jsonArray.map {
                LinkTarget(JsonFields.string(it.jsonObject, "url")!!) to (it.jsonObject.getValue("openable").toString() == "true")
            }
            assertEquals(expected, actual.map { it to it.openable }, JsonFields.string(obj, "name"))
        }
    }
}
