package ru.aokazantsev.pult.render

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertIs
import kotlin.test.assertTrue

class MarkdownParserTest {

    @Test
    fun emphasisAndCodeInOneLine() {
        val block = assertIs<RichBlock.Paragraph>(MarkdownParser.parse("a **b** *c* _d_ `e`").single())
        assertEquals(
            listOf(
                RichSpan("a "),
                RichSpan("b", bold = true),
                RichSpan(" "),
                RichSpan("c", italic = true),
                RichSpan(" "),
                RichSpan("d", italic = true),
                RichSpan(" "),
                RichSpan("e", code = true),
            ),
            block.spans,
        )
    }

    @Test
    fun snakeCaseIsNotItalic() {
        val block = assertIs<RichBlock.Paragraph>(MarkdownParser.parse("snake_case_name").single())
        assertEquals(listOf(RichSpan("snake_case_name")), block.spans)
    }

    @Test
    fun headingsListsAndQuotes() {
        val blocks = MarkdownParser.parse("## Title\n- one\n  - two\n1. three\n> quoted")
        assertEquals(RichBlock.Heading(listOf(RichSpan("Title"))), blocks[0])
        assertEquals(RichBlock.ListItem(0, "•", listOf(RichSpan("one"))), blocks[1])
        assertEquals(RichBlock.ListItem(1, "•", listOf(RichSpan("two"))), blocks[2])
        assertEquals(RichBlock.ListItem(0, "1.", listOf(RichSpan("three"))), blocks[3])
        assertEquals(RichBlock.Quote(1, listOf(RichSpan("quoted"))), blocks[4])
    }

    @Test
    fun nestingDeeperThanFourIsFlat() {
        val item = assertIs<RichBlock.ListItem>(MarkdownParser.parse(" ".repeat(20) + "- deep").single())
        assertEquals(3, item.depth)
        val quote = assertIs<RichBlock.Quote>(MarkdownParser.parse(">>>>>>> deep").single())
        assertEquals(4, quote.depth)
    }

    @Test
    fun fencedBlockIsCodeAndUnclosedFenceIsText() {
        val blocks = MarkdownParser.parse("before\n```kotlin\nval x = 1\n```\nafter")
        assertEquals(RichBlock.CodeBlock("val x = 1"), blocks[1])
        val unclosed = MarkdownParser.parse("```\nnot code")
        assertTrue(unclosed.none { it is RichBlock.CodeBlock })
    }

    @Test
    fun tablesAndHtmlStayAsWrittenText() {
        val blocks = MarkdownParser.parse("<b>x</b> | a | b |")
        assertEquals(listOf(RichSpan("<b>x</b> | a | b |")), assertIs<RichBlock.Paragraph>(blocks.single()).spans)
    }

    @Test
    fun linksAreFoundOutsideCodeOnly() {
        val links = MarkdownLinks.of("[a](https://x.example/p) `https://y.example` https://z.example/q.", markdown = true)
        assertEquals(listOf("https://x.example/p", "https://z.example/q"), links.map { it.url })
        assertEquals(emptyList(), MarkdownLinks.of("https://z.example", markdown = false))
    }
}
