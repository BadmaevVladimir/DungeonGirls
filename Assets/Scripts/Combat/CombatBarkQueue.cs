// Реплики в бою: кто и что говорит в конкретный момент. Отделено от рисования намеренно —
// тайминги, бабл и корутина живут в RunFlowController, а здесь только правила очереди, поэтому
// вся таблица решений проверяется EditMode-тестами без сцены (см. CombatBarkQueueTests).

public enum BarkSpeaker
{
    Hero,
    Boss
}

// Одна заявка на реплику. PhaseIndex нужен не для показа, а для протухания: пока заявка ждала
// закрытия оверлея, босс мог уйти в следующую фазу, и объяснять прошлую уже поздно.
public readonly struct BarkRequest
{
    public readonly BarkSpeaker Speaker;
    public readonly string Text;
    public readonly EncounterBarkTrigger Trigger;
    public readonly int PhaseIndex;

    public BarkRequest(BarkSpeaker speaker, string text, EncounterBarkTrigger trigger, int phaseIndex)
    {
        Speaker = speaker;
        Text = text;
        Trigger = trigger;
        PhaseIndex = phaseIndex;
    }
}

// Состояние мира на текущий кадр — всё, что очередь должна знать, чтобы решить, можно ли
// начинать. Приходит снаружи, чтобы очередь не лезла ни в Time, ни в TutorialManager.
public struct BarkGate
{
    // Открыт туториал, справка или любой другой оверлей: бабл за ним не увидят.
    public bool OverlayVisible;
    // Time.timeScale <= 0 по любой причине.
    public bool Paused;
    public bool CombatRunning;
    public int CurrentBossPhaseIndex;
}

public class CombatBarkQueue
{
    BarkRequest? pending;
    BarkRequest? active;

    public bool HasActive => active.HasValue;

    // Текущую реплику нужно оборвать досрочно: пришло что-то важнее. Читается рисующей стороной.
    public bool InterruptRequested { get; private set; }

    // Смена фазы важнее приветствия: она объясняет игроку то, что происходит прямо сейчас.
    static int Priority(BarkRequest request) =>
        request.Trigger == EncounterBarkTrigger.PhaseChanged ? 2 : 1;

    public void Enqueue(BarkRequest request)
    {
        if (active.HasValue && Priority(request) > Priority(active.Value))
        {
            InterruptRequested = true;
            pending = request;
            return;
        }

        // Ожидающая реплика ровно одна. Равная по важности не вытесняет: первая заявка описывает
        // то, что случилось раньше, и копить их бессмысленно — показать успеем только одну.
        if (!pending.HasValue || Priority(request) > Priority(pending.Value))
        {
            pending = request;
        }
    }

    public bool TryTake(BarkGate gate, out BarkRequest request)
    {
        request = default;
        if (active.HasValue || !pending.HasValue) return false;

        if (IsStale(pending.Value, gate))
        {
            pending = null;
            return false;
        }

        // Оверлей и пауза не отменяют реплику, а задерживают: она дождётся своего момента.
        if (gate.OverlayVisible || gate.Paused) return false;

        request = pending.Value;
        pending = null;
        active = request;
        return true;
    }

    public void MarkFinished()
    {
        active = null;
        InterruptRequested = false;
    }

    public void Reset()
    {
        active = null;
        pending = null;
        InterruptRequested = false;
    }

    static bool IsStale(BarkRequest request, BarkGate gate)
    {
        if (!gate.CombatRunning) return true;
        return request.Trigger == EncounterBarkTrigger.PhaseChanged
            && request.PhaseIndex != gate.CurrentBossPhaseIndex;
    }
}
