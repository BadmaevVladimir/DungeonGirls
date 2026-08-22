// Рантайм-состояние одного оружия в бою: свой урон, тип урона, скорость атаки и таймер.
// У монстра и персонажа с одним оружием их ровно одно; у персонажа с двумя оружиями в руках —
// два независимых экземпляра (3.9 "Амбидекстрия": у каждого оружия свой таймер атаки по своей
// собственной скорости атаки, бьют независимо друг от друга).
public class WeaponAttackState
{
    public float DamageMin;
    public float DamageMax;
    public DamageType DamageType;
    public float AttackSpeed;
    public float AttackTimer;
}
