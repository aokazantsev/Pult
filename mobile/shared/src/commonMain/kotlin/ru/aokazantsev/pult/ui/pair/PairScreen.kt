package ru.aokazantsev.pult.ui.pair

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import ru.aokazantsev.pult.designsystem.component.PultError
import ru.aokazantsev.pult.designsystem.component.PultLoading
import ru.aokazantsev.pult.designsystem.component.PultScaffold
import ru.aokazantsev.pult.designsystem.component.PultTopBar
import ru.aokazantsev.pult.pairing.PairingProgress

@Composable
fun PairScreen(
    viewModel: PairViewModel,
    scanner: @Composable (modifier: Modifier, onScanned: (String) -> Unit) -> Unit,
    onBack: () -> Unit,
    onPaired: () -> Unit,
) {
    val progress by viewModel.progress.collectAsState()
    val pair by viewModel.pair.collectAsState()
    LaunchedEffect(pair) { if (pair != null) onPaired() }
    PultScaffold(topBar = { PultTopBar(title = "Сопряжение", onBack = onBack) }) { padding ->
        Box(modifier = Modifier.padding(padding).fillMaxSize()) {
            when (val current = progress) {
                PairingProgress.Idle -> scanner(Modifier.fillMaxSize(), viewModel::onScanned)
                is PairingProgress.Working -> PultLoading(text = current.step)
                is PairingProgress.Failed -> PultError(
                    message = current.reason,
                    actionText = "Сканировать снова",
                    onAction = viewModel::retry,
                )
            }
        }
    }
}
