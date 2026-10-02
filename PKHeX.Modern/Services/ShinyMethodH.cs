using System;
using System.Diagnostics;
using PKHeX.Core;

namespace PKHeX.Modern.Services;

/// <summary>
/// Shiny selvagem da Gen 3 com sequencia (frame) que o jogo realmente pode gerar.
/// </summary>
/// <remarks>
/// O gerador do Core (<see cref="GenerateMethodH.SetRandom"/>) cria shiny descartando PIDs que ja tinham a natureza
/// certa so por nao serem shiny; o jogo nunca faz isso, entao a analise marca "Unable to match encounter conditions
/// to a possible RNG frame" e "(❌)" no Origin Seed. Aqui seguimos a regra do jogo (Method H, sem lead):
/// o primeiro PID com a natureza sorteada e o que fica; so aceitamos o frame se esse PID ja for shiny.
/// </remarks>
public static class ShinyMethodH
{
    /// <summary>Tenta gerar PID/IVs/nivel shiny para o encontro. Retorna false se nao achar dentro do tempo.</summary>
    public static bool TrySetShiny(PK3 pk, IEncounterSlot3 enc, Nature nature, Gender gender, TimeSpan budget)
    {
        var pi = PersonalTable.E[enc.Species];
        var id32 = pk.ID32;
        var (min, max) = SlotMethodH.GetRange(enc.Type, enc.SlotNumber);
        bool checkProc = MethodH.IsEncounterCheckApplicable(enc.Type);
        var sw = Stopwatch.StartNew();
        var frame = Util.Rand32();

        while (sw.Elapsed < budget)
        {
            for (int i = 0; i < 100_000; i++, frame = LCRNG.Next(frame))
            {
                var seed = frame;
                if (checkProc && !MethodH.CheckEncounterActivation(enc, seed, LeadRequired.None, out _))
                    continue;
                var esv = LCRNG.Next16(ref seed) % 100;
                if (esv < min || esv > max)
                    continue;
                var lv = LCRNG.Next16(ref seed);
                var nat = LCRNG.Next16(ref seed) % 25;
                if (nature != Nature.Random && nat != (uint)nature)
                    continue;

                // O jogo rola PIDs ate a natureza bater e fica com o primeiro.
                uint pid;
                do
                {
                    var a = LCRNG.Next16(ref seed);
                    var b = LCRNG.Next16(ref seed);
                    pid = GenerateMethodH.GetPIDRegular(a, b);
                } while (pid % 25 != nat);

                if (!ShinyUtil.GetIsShiny3(id32, pid))
                    continue;
                if (gender != Gender.Random && EntityGender.GetFromPIDAndRatio(pid, pi.Gender) != (byte)gender)
                    continue;

                var iv32 = ClassicEraRNG.GetSequentialIVs(ref seed);
                var level = (byte)MethodH.GetRandomLevel(enc, lv, LeadRequired.None);
                pk.MetLevel = pk.CurrentLevel = level;
                pk.PID = pid;
                pk.IV32 = iv32;
                pk.RefreshAbility((int)(pid & 1));
                pk.RefreshChecksum();
                return true;
            }
        }
        return false;
    }
}
