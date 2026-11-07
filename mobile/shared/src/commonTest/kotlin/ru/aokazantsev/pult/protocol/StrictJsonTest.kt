package ru.aokazantsev.pult.protocol

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertNull

class StrictJsonTest {

    @Test
    fun duplicateKeysAreRejectedAtAnyDepth() {
        assertFailsWith<StrictJsonException> { StrictJson.parse("""{"a":1,"a":2}""") }
        assertFailsWith<StrictJsonException> { StrictJson.parse("""{"a":{"b":1,"b":2}}""") }
        assertFailsWith<StrictJsonException> { StrictJson.parse("""[{"a":1,"a":1}]""") }
    }

    @Test
    fun nonJsonNumbersAndLiteralsAreRejected() {
        listOf(
            """{"a":NaN}""", """{"a":Infinity}""", """{"a":01}""", """{"a":1.}""", """{"a":+1}""",
            """{"a":tru}""", """{"a":1} x""", """{'a':1}""", """{"a":1,}""",
        ).forEach { text -> assertFailsWith<StrictJsonException>(text) { StrictJson.parse(text) } }
    }

    @Test
    fun parsesNestedValuesAndKeepsNumberLiterals() {
        val obj = StrictJson.parseObject("""{"s":"a\nЖ","n":600.0,"i":7,"x":[true,null,{"k":"v"}]}""")
        assertEquals("a\nЖ", JsonFields.string(obj["s"]))
        assertNull(JsonFields.integer(obj["n"]))
        assertEquals(7L, JsonFields.integer(obj["i"]))
    }

    @Test
    fun integerFieldsAreCanonicalOnly() {
        val obj = StrictJson.parseObject(
            """{"a":"1","b":2.0,"c":-1,"d":9007199254740992,"e":9007199254740991,"f":0,"g":true}""",
        )
        listOf("a", "b", "c", "d", "g").forEach { assertNull(JsonFields.integer(obj[it]), it) }
        assertEquals(9007199254740991L, JsonFields.integer(obj["e"]))
        assertEquals(0L, JsonFields.integer(obj["f"]))
    }

    @Test
    fun controlCharactersInStringsAreRejected() {
        assertFailsWith<StrictJsonException> { StrictJson.parse("{\"a\":\"x\ny\"}") }
    }
}
