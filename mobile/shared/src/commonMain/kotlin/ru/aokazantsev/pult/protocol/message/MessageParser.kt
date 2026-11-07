package ru.aokazantsev.pult.protocol.message

import kotlinx.serialization.json.JsonObject
import ru.aokazantsev.pult.protocol.JsonFields
import ru.aokazantsev.pult.protocol.Outcome

object MessageParser {

    fun parseFromPc(inner: JsonObject): ParseResult = try {
        ParseResult.Accepted(parseChecked(inner))
    } catch (rejected: MessageRejected) {
        ParseResult.Rejected(rejected.reason)
    }

    private fun parseChecked(inner: JsonObject): PcMessage {
        FieldReader.rejectNulls(inner)
        if (ProtocolText.serializedBytes(inner) > MESSAGE_BYTES) FieldReader.reject("message too large")
        val fields = FieldReader(inner)
        val type = JsonFields.string(inner["type"]) ?: FieldReader.reject("bad type")
        return when (type) {
            "thread" -> PcMessage.ThreadUpdate(ThreadParser.parse(inner, inSnapshot = false))
            "feed" -> parseFeed(fields)
            "permission_request", "choice_request", "text_request" -> RequestParser.parse(inner)
            "resolved" -> PcMessage.Resolved(
                requestId = fields.ident("request_id"),
                outcome = Outcome.fromWire(fields.text("outcome", ENUM_LIMIT)),
            )
            "mode" -> PcMessage.Mode(PultMode.fromWire(fields.text("mode", ENUM_LIMIT)))
            "snapshot" -> parseSnapshot(fields)
            "hello" -> parseHello(fields)
            in PHONE_ONLY_TYPES -> FieldReader.reject("type not for phone")
            else -> PcMessage.Unknown(type)
        }
    }

    private fun parseFeed(fields: FieldReader): PcMessage.Feed {
        val thread = fields.ident("thread")
        val entries = fields.array("entries", minimum = 1).map(FeedEntryParser::parse)
        entries.zipWithNext { previous, next ->
            if (next.seq != previous.seq + 1) FieldReader.reject("seq not contiguous")
        }
        return PcMessage.Feed(thread, entries)
    }

    private fun parseSnapshot(fields: FieldReader): PcMessage.Snapshot {
        val snapshot = fields.ident("snapshot")
        val part = fields.integer("part", minimum = 1)
        val last = fields.boolean("last")
        val mode = if (part == 1L) {
            PultMode.fromWire(fields.text("mode", ENUM_LIMIT))
        } else {
            fields.textOrNull("mode", ENUM_LIMIT)?.let(PultMode::fromWire)
        }
        return PcMessage.Snapshot(
            snapshot = snapshot,
            part = part,
            last = last,
            mode = mode,
            threads = fields.arrayOrNull("threads").orEmpty().map { ThreadParser.parse(it, inSnapshot = true) },
            requests = fields.arrayOrNull("requests").orEmpty().map(RequestParser::parse),
            outcomes = fields.arrayOrNull("outcomes").orEmpty().map(OutcomeParser::parse),
        )
    }

    private fun parseHello(fields: FieldReader): PcMessage.Hello {
        val protocol = fields.array("protocol", minimum = 1).map { JsonFields.integer(it) ?: FieldReader.reject("bad protocol") }
        val capabilities = fields.array("capabilities").map { JsonFields.string(it) ?: FieldReader.reject("bad capability") }
        if (fields.has("feeds")) FieldReader.reject("feeds only from phone")
        return PcMessage.Hello(protocol, capabilities)
    }

    private const val MESSAGE_BYTES = 48_000
    private const val ENUM_LIMIT = 64
    private val PHONE_ONLY_TYPES = setOf("permission_response", "choice_response", "text_response")
}
