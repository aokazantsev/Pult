package ru.aokazantsev.pult.ui.pair

import androidx.lifecycle.ViewModel
import dev.zacsweers.metro.Inject
import kotlinx.coroutines.flow.StateFlow
import ru.aokazantsev.pult.pairing.PairStore
import ru.aokazantsev.pult.pairing.PairingController
import ru.aokazantsev.pult.pairing.PairingProgress
import ru.aokazantsev.pult.pairing.StoredPair

@Inject
class PairViewModel(
    private val controller: PairingController,
    store: PairStore,
) : ViewModel() {

    val progress: StateFlow<PairingProgress> = controller.progress

    val pair: StateFlow<StoredPair?> = store.pair

    fun onScanned(text: String) = controller.submit(text)

    fun retry() = controller.dismiss()
}
