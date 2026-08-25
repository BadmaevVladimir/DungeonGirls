// Assets/Scripts/Data/MentorData.cs
using UnityEngine;

// 1, п.3: наставник передаёт СВОЙ ОСНОВНОЙ пассивный навык напрямую (сразу и всегда, без
// возможности прокачки при передаче) + добавляет ОСТАЛЬНЫЕ известные ему навыки в пул вариантов
// окна левел-апа нового персонажа (кросс-классовость — намеренная фича, см. 3.5). Для прототипа —
// один наставник-заглушка класса Маг, чей единственный известный навык — как раз основной
// пассивный ("Магнум Опус") — поэтому otherKnownSkills для прототипа пуст (нечего добавлять в пул).
[CreateAssetMenu(fileName = "NewMentor", menuName = "DungeonGirls/Mentor")]
public class MentorData : ScriptableObject
{
    public string mentorName;
    public CharacterClass mentorClass;

    // Для отображения/лога — сама механика применяется через mainPassiveMagicDamageBonusPercent
    // (см. design note в плане реализации: не левелится, поэтому не хранится как обычная
    // прокачиваемая запись в RunCharacterProgress.KnownSkillLevels).
    public PassiveSkillData mainPassiveSkill;
    public float mainPassiveMagicDamageBonusPercent;

    public PassiveSkillData[] otherKnownSkills;
}
