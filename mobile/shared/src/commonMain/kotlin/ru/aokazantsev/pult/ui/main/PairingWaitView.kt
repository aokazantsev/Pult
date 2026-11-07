package ru.aokazantsev.pult.ui.main

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.produceState
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextAlign
import kotlinx.coroutines.delay
import ru.aokazantsev.pult.designsystem.component.PultCard
import ru.aokazantsev.pult.designsystem.component.PultSecondaryButton
import ru.aokazantsev.pult.designsystem.component.PultText
import ru.aokazantsev.pult.designsystem.theme.PultTheme
import ru.aokazantsev.pult.pairing.PairInfo
import kotlin.time.Clock

@Composable
fun PairingWaitView(info: PairInfo, onCancel: () -> Unit, modifier: Modifier = Modifier) {
    val secondsLeft by produceState(initialValue = remaining(info), info.helloDeadlineEpochMs) {
        while (true) {
            value = remaining(info)
            delay(TICK_MS)
        }
    }
    Column(
        modifier = modifier.padding(PultTheme.spacing.l),
        verticalArrangement = Arrangement.spacedBy(PultTheme.spacing.m),
    ) {
        PultCard {
            PultText(text = "Сверьте отпечаток с экраном ПК", style = PultTheme.typography.titleMedium)
            PultText(
                text = info.fingerprint,
                style = PultTheme.typography.headlineMedium,
                textAlign = TextAlign.Center,
                modifier = Modifier.fillMaxWidth(),
            )
            PultText(
                text = "Ждём подтверждения от ПК: $secondsLeft с",
                color = PultTheme.colors.onSurfaceVariant,
            )
            if (info.softwareKeys) {
                PultText(text = SOFTWARE_KEYS_WARNING, color = PultTheme.colors.error)
            }
        }
        PultSecondaryButton(text = "Отменить сопряжение", onClick = onCancel, modifier = Modifier.fillMaxWidth())
    }
}

const val SOFTWARE_KEYS_WARNING = "Ключи не в аппаратном хранилище телефона: защита слабее, сессия работает"

private const val TICK_MS = 500L

private fun remaining(info: PairInfo): Long =
    ((info.helloDeadlineEpochMs - Clock.System.now().toEpochMilliseconds()) / 1000).coerceAtLeast(0)
