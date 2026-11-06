package ru.aokazantsev.pult.ui.main

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import ru.aokazantsev.pult.designsystem.component.PultCard
import ru.aokazantsev.pult.designsystem.component.PultPrimaryButton
import ru.aokazantsev.pult.designsystem.component.PultSecondaryButton
import ru.aokazantsev.pult.designsystem.component.PultText
import ru.aokazantsev.pult.designsystem.component.PultTextField
import ru.aokazantsev.pult.designsystem.theme.PultTheme
import ru.aokazantsev.pult.protocol.InboundMessage
import ru.aokazantsev.pult.protocol.Outcome
import ru.aokazantsev.pult.session.CardPhase
import ru.aokazantsev.pult.session.RequestCard

@Composable
fun RequestCardView(
    card: RequestCard,
    onAllow: () -> Unit,
    onDeny: () -> Unit,
    onChoose: (List<String>) -> Unit,
    onSendText: (String) -> Unit,
) {
    val interactive = card.phase == CardPhase.Pending
    PultCard(dimmed = card.phase == CardPhase.Closed) {
        when (val message = card.message) {
            is InboundMessage.Permission -> {
                Header(message.title, message.thread)
                PultText(text = "${message.tool}: ${message.summary}", style = PultTheme.typography.titleMedium)
                if (message.input.isNotBlank()) {
                    PultText(
                        text = message.input,
                        style = PultTheme.typography.code,
                        color = PultTheme.colors.onSurfaceVariant,
                        maxLines = INPUT_MAX_LINES,
                    )
                }
                if (interactive) {
                    Row(horizontalArrangement = Arrangement.spacedBy(PultTheme.spacing.s)) {
                        PultPrimaryButton(text = "Разрешить", onClick = onAllow)
                        PultSecondaryButton(text = "Запретить", onClick = onDeny)
                    }
                }
            }
            is InboundMessage.Choice -> {
                Header(message.title, message.thread)
                PultText(text = message.question, style = PultTheme.typography.titleMedium)
                if (interactive) ChoiceActions(message, onChoose)
            }
            is InboundMessage.Text -> {
                Header(message.title, message.thread)
                PultText(text = message.lastMessage, maxLines = LAST_MESSAGE_MAX_LINES)
                if (interactive) TextActions(onSendText)
            }
            is InboundMessage.Unknown -> PultText(text = "Неизвестный тип: ${message.type}")
            else -> Unit
        }
        StatusLine(card)
    }
}

@Composable
private fun Header(title: String, thread: String) {
    PultText(
        text = title.ifBlank { thread },
        style = PultTheme.typography.labelLarge,
        color = PultTheme.colors.onSurfaceVariant,
        maxLines = 1,
    )
}

@Composable
private fun ChoiceActions(message: InboundMessage.Choice, onChoose: (List<String>) -> Unit) {
    var selected by remember { mutableStateOf(emptyList<String>()) }
    message.options.forEach { option ->
        val checked = option.id in selected
        val label = if (checked) "✓ ${option.label}" else option.label
        val action = {
            if (message.multi) {
                selected = if (checked) selected - option.id else selected + option.id
            } else {
                onChoose(listOf(option.id))
            }
        }
        if (checked) {
            PultPrimaryButton(text = label, onClick = action, modifier = Modifier.fillMaxWidth())
        } else {
            PultSecondaryButton(text = label, onClick = action, modifier = Modifier.fillMaxWidth())
        }
    }
    if (message.multi) {
        PultPrimaryButton(text = "Отправить", onClick = { onChoose(selected) }, enabled = selected.isNotEmpty())
    }
}

@Composable
private fun TextActions(onSendText: (String) -> Unit) {
    var text by remember { mutableStateOf("") }
    PultTextField(
        value = text,
        onValueChange = { text = it },
        label = "Ответ",
        singleLine = false,
        minLines = 2,
        modifier = Modifier.fillMaxWidth(),
    )
    PultPrimaryButton(text = "Отправить", onClick = { onSendText(text) }, enabled = text.isNotBlank())
}

@Composable
private fun StatusLine(card: RequestCard) {
    val text = when (card.phase) {
        CardPhase.Pending -> card.error?.let { "Не отправлено: $it" }
        CardPhase.Sending -> "Отправка…"
        CardPhase.Sent -> "Отправлено, ждём ПК"
        CardPhase.Closed -> outcomeText(card.outcome)
    } ?: return
    val color = if (card.phase == CardPhase.Pending) PultTheme.colors.error else PultTheme.colors.onSurfaceVariant
    PultText(text = text, style = PultTheme.typography.labelMedium, color = color)
}

private fun outcomeText(outcome: Outcome?): String = when (outcome) {
    Outcome.Answered -> "Ответ принят"
    Outcome.Expired -> "Истёк"
    Outcome.ResolvedOnPc -> "Решено на ПК"
    Outcome.Unknown, null -> "Исход неизвестен"
}

private const val INPUT_MAX_LINES = 6
private const val LAST_MESSAGE_MAX_LINES = 12
