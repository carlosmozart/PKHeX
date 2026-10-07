using System;
using PKHeX.Core;

namespace PKHeX.Modern.Services;

/// <summary>
/// Roamers de RS/FRLG (Entei, Raikou, Suicune, Latias, Latios) com IVs sorteados.
/// </summary>
/// <remarks>
/// Por um bug do jogo, o roamer guarda so 8 bits dos IVs (PS 0-31 e Atq 0-7; o resto fica 0). O gerador do Core
/// testa esses 256 valores a partir do zero e fica com o primeiro aceito, entao sem filtro de IVs todo roamer saia
/// com IVs zerados. Aqui o valor de 8 bits e sorteado e o PID vem da mesma sequencia Method 1 que o jogo usa.
/// </remarks>
public static class RoamerIVs3
{
    public static bool IsTruncated(IEncounterInfo enc) => enc is EncounterStatic3 { IsRoamingTruncatedIVs: true };

    /// <summary>Sorteia de novo IVs e PID do roamer, respeitando natureza, shiny e IVs pedidos.</summary>
    public static bool Reroll(PK3 pk, in EncounterCriteria criteria)
    {
        var id32 = pk.ID32;
        var start = (uint)Random.Shared.Next(256);
        for (uint n = 0; n <= byte.MaxValue; n++)
        {
            var iv32 = (start + n) & 0xFF;
            if (!criteria.IsSatisfiedIVs(iv32))
                continue;
            var frame = iv32 << 16;
            var offset = (uint)Random.Shared.Next(1 << 24);
            for (uint k = 0; k < 1u << 24; k++)
            {
                // Bits altos do primeiro rand de IV (que nao sao guardados) e os 16 bits baixos do estado: livres.
                var free = (offset + k) & 0xFFFFFF;
                var state = frame | ((free >> 16) << 24) | (free & 0xFFFF);
                var rand2 = LCRNG.Prev16(ref state);
                var rand1 = LCRNG.Prev16(ref state);
                var pid = (rand2 << 16) | rand1;
                if (criteria.IsSpecifiedNature() && !criteria.IsSatisfiedNature(pid))
                    continue;
                if (criteria.Shiny.IsShiny() != ShinyUtil.GetIsShiny3(id32, pid))
                    continue;
                pk.PID = pid;
                pk.IV32 = iv32;
                pk.RefreshAbility(0);
                pk.ResetPartyStats();
                return true;
            }
        }
        return false;
    }
}
