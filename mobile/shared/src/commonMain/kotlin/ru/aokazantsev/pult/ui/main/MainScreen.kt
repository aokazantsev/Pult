package ru.aokazantsev.pult.ui.main

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LifecycleEventEffect
import ru.aokazantsev.pult.designsystem.component.PultCard
import ru.aokazantsev.pult.designsystem.component.PultError
import ru.aokazantsev.pult.designsystem.component.PultIconButton
import ru.aokazantsev.pult.designsystem.component.PultScaffold
import ru.aokazantsev.pult.designsystem.component.PultText
import ru.aokazantsev.pult.designsystem.component.PultTopBar
import ru.aokazantsev.pult.designsystem.icon.PultIcons
import ru.aokazantsev.pult.designsystem.theme.PultTheme
import ru.aokazantsev.pult.session.ConnectionState

@Composable
fun MainScreen(viewModel: MainViewModel, onOpenSettings: () -> Unit, onStartPairing: () -> Unit) {
    LifecycleEventEffect(Lifecycle.Event.ON_START) { viewModel.onScreenStart() }
    LifecycleEventEffect(Lifecycle.Event.ON_STOP) { viewModel.onScreenStop() }
    val state by viewModel.state.collectAsState()
    val pairInfo = state.pairInfo
    PultScaffold(
        topBar = {
            PultTopBar(
                title = "Pult",
                subtitle = connectionText(state.connection),
                actions = {
                    PultIconButton(icon = PultIcons.Settings, contentDescription = "Настройка", onClick = onOpenSettings)
                },
            )
        },
    ) { padding ->
        val connection = state.connection
        when {
            pairInfo != null && !pairInfo.confirmed -> PairingWaitView(
                info = pairInfo,
                onCancel = viewModel::cancelPairing,
                modifier = Modifier.padding(padding),
            )
            connection is ConnectionState.PairEnded -> PultError(
                message = connection.reason,
                actionText = "Сопрячь заново",
                onAction = onStartPairing,
                modifier = Modifier.padding(padding),
            )
            connection is ConnectionState.NotPaired -> PultError(
                message = "Телефон не сопряжён с ПК",
                actionText = "Сопрячь с ПК",
                onAction = onStartPairing,
                modifier = Modifier.padding(padding),
            )
            connection is ConnectionState.Incompatible -> PultError(
                message = connection.reason,
                modifier = Modifier.padding(padding),
            )
            state.cards.isEmpty() -> PultError(
                message = "Запросов нет. ${viewModel.versionText}",
                modifier = Modifier.padding(padding),
            )
            else -> LazyColumn(
                modifier = Modifier.padding(padding),
                contentPadding = PaddingValues(PultTheme.spacing.l),
                verticalArrangement = Arrangement.spacedBy(PultTheme.spacing.m),
            ) {
                val notes = listOfNotNull(
                    state.notice,
                    state.dropped.takeIf { it > 0 }?.let { "Отброшено сообщений, не прошедших проверку: $it" },
                )
                if (notes.isNotEmpty()) {
                    item(key = "notes") {
                        PultCard {
                            notes.forEach { PultText(text = it, color = PultTheme.colors.error) }
                        }
                    }
                }
                items(state.cards.asReversed(), key = { it.key }) { card ->
                    RequestCardView(
                        card = card,
                        onAllow = { viewModel.allow(card.key) },
                        onDeny = { viewModel.deny(card.key) },
                        onChoose = { viewModel.choose(card.key, it) },
                        onSendText = { viewModel.sendText(card.key, it) },
                    )
                }
            }
        }
    }
}

private fun connectionText(connection: ConnectionState): String = when (connection) {
    ConnectionState.NotPaired -> "не сопряжён"
    ConnectionState.Idle -> "не на связи"
    ConnectionState.Connecting -> "подключение…"
    ConnectionState.Connected -> "на связи"
    is ConnectionState.Disconnected -> "обрыв: ${connection.reason}"
    is ConnectionState.Incompatible -> "несовместимость версий"
    is ConnectionState.PairEnded -> "пара разорвана"
}
