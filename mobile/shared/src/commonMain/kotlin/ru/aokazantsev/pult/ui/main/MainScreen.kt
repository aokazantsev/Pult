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
import ru.aokazantsev.pult.designsystem.component.PultError
import ru.aokazantsev.pult.designsystem.component.PultIconButton
import ru.aokazantsev.pult.designsystem.component.PultScaffold
import ru.aokazantsev.pult.designsystem.component.PultText
import ru.aokazantsev.pult.designsystem.component.PultTopBar
import ru.aokazantsev.pult.designsystem.icon.PultIcons
import ru.aokazantsev.pult.designsystem.theme.PultTheme
import ru.aokazantsev.pult.session.ConnectionState

@Composable
fun MainScreen(viewModel: MainViewModel, onOpenSettings: () -> Unit) {
    LifecycleEventEffect(Lifecycle.Event.ON_START) { viewModel.onScreenStart() }
    LifecycleEventEffect(Lifecycle.Event.ON_STOP) { viewModel.onScreenStop() }
    val state by viewModel.state.collectAsState()
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
        when {
            state.connection == ConnectionState.NotConfigured -> PultError(
                message = "Сервер не настроен",
                actionText = "Настроить",
                onAction = onOpenSettings,
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
    ConnectionState.NotConfigured -> "не настроен"
    ConnectionState.Idle -> "не на связи"
    ConnectionState.Connecting -> "подключение…"
    ConnectionState.Connected -> "на связи"
    is ConnectionState.Disconnected -> "обрыв: ${connection.reason}"
}
