package ru.aokazantsev.pult.protocol.message

import kotlinx.serialization.json.JsonElement

internal object FeedEntryParser {

    fun parse(element: JsonElement): FeedEntry {
        val fields = FieldReader.item(element, "entry")
        val seq = fields.integer("seq", minimum = 1)
        val kind = fields.text("kind", ENUM_LIMIT)
        val at = fields.integerOrNull("at")
        val truncated = fields.booleanOrNull("truncated") ?: false
        return when (kind) {
            "user" -> FeedEntry.User(
                seq = seq,
                at = at,
                truncated = truncated,
                text = fields.text("text", TEXT_LIMIT),
                viaPhone = fields.textOrNull("via", ENUM_LIMIT) == VIA_PHONE,
                requestId = fields.identOrNull("request_id"),
            )
            "assistant" -> FeedEntry.Assistant(seq, at, truncated, fields.text("text", TEXT_LIMIT))
            "tool_call" -> FeedEntry.ToolCall(
                seq = seq,
                at = at,
                truncated = truncated,
                call = fields.ident("call"),
                tool = fields.text("tool", ENUM_LIMIT),
                summary = fields.text("summary", SUMMARY_LIMIT),
                input = fields.raw("input")?.let(ToolArgsParser::parse).orEmpty(),
            )
            "tool_result" -> FeedEntry.ToolResult(
                seq = seq,
                at = at,
                truncated = truncated,
                call = fields.ident("call"),
                output = fields.text("output", OUTPUT_LIMIT),
                error = fields.boolean("error"),
            )
            "peer" -> FeedEntry.Peer(
                seq = seq,
                at = at,
                truncated = truncated,
                direction = PeerDirection.fromWire(fields.text("direction", ENUM_LIMIT)),
                peerTitle = fields.text("peer_title", TITLE_LIMIT),
                text = fields.text("text", TEXT_LIMIT),
                peerThread = fields.identOrNull("peer"),
            )
            "notice" -> FeedEntry.Notice(seq, at, truncated, fields.text("text", NOTICE_LIMIT))
            "unrecognized" -> FeedEntry.Unrecognized(seq, at, truncated, fields.integer("count", minimum = 1))
            else -> FeedEntry.Unrecognized(seq, at, truncated, count = 1)
        }
    }

    private const val ENUM_LIMIT = 64
    private const val TEXT_LIMIT = 8000
    private const val SUMMARY_LIMIT = 300
    private const val OUTPUT_LIMIT = 2000
    private const val NOTICE_LIMIT = 300
    private const val TITLE_LIMIT = 120
    private const val VIA_PHONE = "phone"
}
