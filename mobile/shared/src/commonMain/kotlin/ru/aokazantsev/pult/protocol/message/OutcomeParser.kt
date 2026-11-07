package ru.aokazantsev.pult.protocol.message

import kotlinx.serialization.json.JsonElement
import ru.aokazantsev.pult.protocol.Outcome

internal object OutcomeParser {

    fun parse(element: JsonElement): ClosedOutcome {
        val fields = FieldReader.item(element, "outcome")
        return ClosedOutcome(
            requestId = fields.ident("request_id"),
            kind = RequestKind.fromWire(fields.text("kind", ENUM_LIMIT)),
            thread = fields.ident("thread"),
            afterSeq = fields.integer("after_seq"),
            summary = fields.text("summary", SUMMARY_LIMIT),
            outcome = Outcome.fromWire(fields.text("outcome", ENUM_LIMIT)),
        )
    }

    private const val ENUM_LIMIT = 64
    private const val SUMMARY_LIMIT = 120
}
