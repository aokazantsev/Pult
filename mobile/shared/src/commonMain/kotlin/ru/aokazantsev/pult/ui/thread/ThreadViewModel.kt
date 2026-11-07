package ru.aokazantsev.pult.ui.thread

import androidx.lifecycle.ViewModel
import dev.zacsweers.metro.Inject
import kotlinx.coroutines.flow.StateFlow
import ru.aokazantsev.pult.protocol.message.ChoiceAnswer
import ru.aokazantsev.pult.session.RelaySession
import ru.aokazantsev.pult.session.SessionState

@Inject
class ThreadViewModel(private val session: RelaySession) : ViewModel() {

    val state: StateFlow<SessionState> = session.state

    fun allow(key: String) = session.answerPermission(key, allow = true)

    fun deny(key: String) = session.answerPermission(key, allow = false)

    fun choose(key: String, answers: List<ChoiceAnswer>) = session.answerChoice(key, answers)

    fun sendText(key: String, text: String) = session.answerText(key, text)
}
