package ru.aokazantsev.pult.ui.thread

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.listSaver
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import ru.aokazantsev.pult.designsystem.component.PultCard
import ru.aokazantsev.pult.designsystem.component.PultPrimaryButton
import ru.aokazantsev.pult.designsystem.component.PultSecondaryButton
import ru.aokazantsev.pult.designsystem.component.PultTextField
import ru.aokazantsev.pult.designsystem.theme.PultTheme
import ru.aokazantsev.pult.protocol.OutboundMessages
import ru.aokazantsev.pult.protocol.Outcome
import ru.aokazantsev.pult.protocol.message.ChoiceAnswer
import ru.aokazantsev.pult.protocol.message.ProtocolText
import ru.aokazantsev.pult.protocol.message.Request
import ru.aokazantsev.pult.render.TextSanitizer
import ru.aokazantsev.pult.session.CardState
import ru.aokazantsev.pult.session.RequestCard
import ru.aokazantsev.pult.ui.Labels
import ru.aokazantsev.pult.ui.render.UntrustedLine
import ru.aokazantsev.pult.ui.render.UntrustedMarkdown
import ru.aokazantsev.pult.ui.render.UntrustedMono
import ru.aokazantsev.pult.ui.render.UntrustedPlain

@Composable
fun RequestCardView(
    card: RequestCard,
    draft: String?,
    onAllow: () -> Unit,
    onDeny: () -> Unit,
    onChoose: (List<ChoiceAnswer>) -> Unit,
    onSendText: (String) -> Unit,
) {
    val interactive = card.state == CardState.Open
    PultCard(dimmed = card.state == CardState.Closed || card.state == CardState.Missing) {
        when (val request = card.request) {
            is Request.Permission -> {
                UntrustedMono(text = "${request.tool}: ${request.summary}")
                request.input.forEach { arg ->
                    SmallLabel(arg.name)
                    UntrustedMono(text = arg.value, color = PultTheme.colors.onSurfaceVariant)
                }
                if (interactive) {
                    Row(horizontalArrangement = Arrangement.spacedBy(PultTheme.spacing.s)) {
                        PultPrimaryButton(text = "Разрешить", onClick = onAllow)
                        PultSecondaryButton(text = "Запретить", onClick = onDeny)
                    }
                }
            }
            is Request.Choice -> ChoiceBody(request, interactive && request.answerable, onChoose)
            is Request.Text -> {
                UntrustedMarkdown(request.lastMessage)
                if (interactive) TextInput(card.requestId, draft, onSendText)
            }
            null -> UntrustedLine(text = "${Labels.kind(card.kind)}: ${card.summary}", maxLines = 2)
        }
        Labels.cardState(card)?.let { text ->
            val failed = card.state == CardState.Open
            UntrustedLine(
                text = text,
                style = PultTheme.typography.labelMedium,
                color = if (failed) PultTheme.colors.error else PultTheme.colors.onSurfaceVariant,
                maxLines = 2,
            )
        }
        val saved = card.sentText
        if (card.state == CardState.Closed && card.outcome != Outcome.Answered && saved != null && saved == draft) {
            SmallLabel("Не отправлено, текст сохранён черновиком:")
            UntrustedPlain(saved)
        }
    }
}

@Composable
private fun ChoiceBody(request: Request.Choice, interactive: Boolean, onChoose: (List<ChoiceAnswer>) -> Unit) {
    var form by rememberSaveable(request.requestId, stateSaver = ChoiceFormSaver) { mutableStateOf(ChoiceFormState()) }
    request.questions.forEach { question ->
        question.header?.let { SmallLabel(it) }
        UntrustedPlain(text = question.question, style = PultTheme.typography.titleSmall)
        val chosen = form.selected[question.id].orEmpty()
        question.options.forEach { option ->
            val safeLabel = TextSanitizer.oneLine(option.label)
            val label = if (option.id in chosen) "✓ $safeLabel" else safeLabel
            val description = option.description?.let { " — $it" }.orEmpty()
            if (interactive) {
                val action = { form = form.toggle(question, option.id) }
                if (option.id in chosen) {
                    PultPrimaryButton(text = label, onClick = action, modifier = Modifier.fillMaxWidth())
                } else {
                    PultSecondaryButton(text = label, onClick = action, modifier = Modifier.fillMaxWidth())
                }
                if (description.isNotEmpty()) SmallLabel(option.description.orEmpty())
            } else {
                UntrustedLine(text = "• ${option.label}$description", maxLines = 2, color = PultTheme.colors.onSurfaceVariant)
            }
        }
        if (interactive && question.other) {
            PultTextField(
                value = form.other[question.id].orEmpty(),
                onValueChange = { form = form.withOther(question, it) },
                label = "Другое",
                singleLine = false,
                modifier = Modifier.fillMaxWidth(),
            )
        }
    }
    if (!request.answerable) {
        SmallLabel("Вопрос стоит в диалоге на ПК: здесь ответить нельзя")
    }
    if (interactive) {
        PultPrimaryButton(
            text = "Отправить",
            onClick = { onChoose(form.answers(request.questions)) },
            enabled = form.isComplete(request),
        )
    }
}

@Composable
private fun TextInput(requestId: String, draft: String?, onSendText: (String) -> Unit) {
    var text by rememberSaveable(requestId) { mutableStateOf(draft.orEmpty()) }
    PultTextField(
        value = text,
        onValueChange = { text = ProtocolText.takeCodePoints(it, OutboundMessages.TEXT_LIMIT) },
        label = "Ответ агенту",
        singleLine = false,
        minLines = 2,
        modifier = Modifier.fillMaxWidth(),
    )
    PultPrimaryButton(
        text = "Отправить",
        onClick = { onSendText(text) },
        enabled = !ProtocolText.isBlank(text),
    )
}

private const val SEPARATOR = "\u0000"

private val ChoiceFormSaver = listSaver<ChoiceFormState, String>(
    save = { state ->
        state.selected.flatMap { (question, options) -> options.map { "s$SEPARATOR$question$SEPARATOR$it" } } +
            state.other.map { (question, text) -> "o$SEPARATOR$question$SEPARATOR$text" }
    },
    restore = { saved ->
        val parts = saved.map { it.split(SEPARATOR, limit = 3) }.filter { it.size == 3 }
        ChoiceFormState(
            selected = parts.filter { it[0] == "s" }.groupBy({ it[1] }, { it[2] }).mapValues { it.value.toSet() },
            other = parts.filter { it[0] == "o" }.associate { it[1] to it[2] },
        )
    },
)
