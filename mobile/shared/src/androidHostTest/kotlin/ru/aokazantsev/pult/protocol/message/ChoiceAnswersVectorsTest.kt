package ru.aokazantsev.pult.protocol.message

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertIs
import kotlin.test.assertTrue
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import ru.aokazantsev.pult.protocol.JsonFields
import ru.aokazantsev.pult.ui.thread.ChoiceFormState
import ru.aokazantsev.pult.vectors.Vectors

class ChoiceAnswersVectorsTest {

    private val section = Vectors.load("messages").getValue("choice_answers").jsonObject
    private val request: Request.Choice = run {
        val parsed = MessageParser.parseFromPc(section.getValue("request").jsonObject)
        assertIs<Request.Choice>(assertIs<ParseResult.Accepted>(parsed).message)
    }

    @Test
    fun acceptedAnswersPassValidation() {
        section.getValue("accept").jsonArray.forEach { response ->
            assertTrue(ChoiceAnswers.isValid(request, answersOf(response)), response.toString())
        }
    }

    @Test
    fun rejectedAnswersFailValidation() {
        section.getValue("reject").jsonArray.forEach { response ->
            assertEquals(false, ChoiceAnswers.isValid(request, answersOf(response)), response.toString())
        }
    }

    @Test
    fun formBuildsTheAcceptedAnswers() {
        section.getValue("accept").jsonArray.forEach { response ->
            val expected = answersOf(response).associateBy { it.question }
            var form = ChoiceFormState()
            request.questions.forEach { question ->
                val answer = expected.getValue(question.id)
                answer.selected.forEach { form = form.toggle(question, it) }
                answer.other?.let { form = form.withOther(question, it) }
            }
            assertTrue(form.isComplete(request), response.toString())
            assertEquals(request.questions.map { expected.getValue(it.id) }, form.answers(request.questions))
        }
    }

    private fun answersOf(response: JsonElement): List<ChoiceAnswer> =
        response.jsonObject.getValue("answers").jsonArray.map { element ->
            val obj = element.jsonObject
            ChoiceAnswer(
                question = JsonFields.string(obj, "question")!!,
                selected = obj.getValue("selected").jsonArray.map { (it as JsonPrimitive).content },
                other = JsonFields.string(obj, "other"),
            )
        }
}
