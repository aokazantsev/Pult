package ru.aokazantsev.pult.protocol.message

import kotlinx.serialization.json.JsonElement

internal object RequestParser {

    fun parse(element: JsonElement): Request {
        val fields = FieldReader.item(element, "request")
        val requestId = fields.ident("request_id")
        val thread = fields.ident("thread")
        val title = fields.text("title", TITLE_LIMIT)
        val afterSeq = fields.integer("after_seq")
        return when (fields.text("type", ENUM_LIMIT)) {
            "permission_request" -> Request.Permission(
                requestId = requestId,
                thread = thread,
                title = title,
                afterSeq = afterSeq,
                tool = fields.text("tool", ENUM_LIMIT),
                summary = fields.text("summary", SUMMARY_LIMIT),
                input = fields.raw("input")?.let(ToolArgsParser::parse).orEmpty(),
            )
            "choice_request" -> Request.Choice(
                requestId = requestId,
                thread = thread,
                title = title,
                afterSeq = afterSeq,
                answerable = fields.boolean("answerable"),
                questions = parseQuestions(fields),
            )
            "text_request" -> Request.Text(
                requestId = requestId,
                thread = thread,
                title = title,
                afterSeq = afterSeq,
                lastMessage = fields.text("last_message", LAST_MESSAGE_LIMIT),
            )
            else -> FieldReader.reject("not a request")
        }
    }

    private fun parseQuestions(fields: FieldReader): List<ChoiceQuestion> {
        val seen = HashSet<String>()
        return fields.array("questions", minimum = 1, maximum = MAX_QUESTIONS).map { element ->
            val question = FieldReader.item(element, "question")
            val id = question.ident("id")
            if (!seen.add(id)) FieldReader.reject("duplicate question id")
            ChoiceQuestion(
                id = id,
                header = question.textOrNull("header", HEADER_LIMIT),
                question = question.text("question", QUESTION_LIMIT),
                multi = question.boolean("multi"),
                other = question.boolean("other"),
                options = parseOptions(question),
            )
        }
    }

    private fun parseOptions(question: FieldReader): List<ChoiceOption> {
        val seen = HashSet<String>()
        return question.array("options", minimum = 1, maximum = MAX_OPTIONS).map { element ->
            val option = FieldReader.item(element, "option")
            val id = option.ident("id")
            if (!seen.add(id)) FieldReader.reject("duplicate option id")
            ChoiceOption(
                id = id,
                label = option.text("label", LABEL_LIMIT),
                description = option.textOrNull("description", DESCRIPTION_LIMIT),
            )
        }
    }

    private const val ENUM_LIMIT = 64
    private const val TITLE_LIMIT = 120
    private const val SUMMARY_LIMIT = 300
    private const val LAST_MESSAGE_LIMIT = 4000
    private const val HEADER_LIMIT = 50
    private const val QUESTION_LIMIT = 500
    private const val LABEL_LIMIT = 100
    private const val DESCRIPTION_LIMIT = 300
    private const val MAX_QUESTIONS = 4
    private const val MAX_OPTIONS = 8
}
