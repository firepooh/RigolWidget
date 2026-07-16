namespace RigolWidget.Visa;

/// <summary>채널 정격: 설정/보호값 클램프 상한.</summary>
public sealed record ChannelRating(double VMax, double IMax, double OvpMax, double OcpMax);

/// <summary>DP800 시리즈 모델 정격(우리가 제어하는 CH1·CH2 기준).</summary>
public sealed record Dp800Model(string Name, ChannelRating Ch1, ChannelRating? Ch2)
{
    /// <summary>2채널 여부(false면 단일 채널 모델 → CH2 숨김).</summary>
    public bool HasCh2 => Ch2 is not null;

    /// <summary>채널 번호로 정격 조회(없으면 CH1 정격 대체).</summary>
    public ChannelRating RatingFor(int channel) => channel == 2 ? (Ch2 ?? Ch1) : Ch1;
}

/// <summary>DP800 시리즈 모델 정격 테이블 및 *IDN? 매칭.</summary>
public static class Dp800Models
{
    // 값 출처: RIGOL DP800 User's Guide Ch.5 Specifications (DC Output / OVP·OCP 범위).
    // 보호 상한(OvpMax/OcpMax)은 스펙의 OVP/OCP 설정 가능 범위 상단값.
    private static readonly Dp800Model[] Table =
    {
        // DP832 / DP832A: CH1 30V/3A, CH2 30V/3A
        new("DP832",  new(30, 3, 33, 3.3),  new(30, 3, 33, 3.3)),
        new("DP832A", new(30, 3, 33, 3.3),  new(30, 3, 33, 3.3)),
        // DP831 / DP831A: CH1 8V/5A, CH2 30V/2A
        new("DP831",  new(8, 5, 8.8, 5.5),  new(30, 2, 33, 2.2)),
        new("DP831A", new(8, 5, 8.8, 5.5),  new(30, 2, 33, 2.2)),
        // DP821 / DP821A: CH1 60V/1A, CH2 8V/10A
        new("DP821",  new(60, 1, 66, 1.1),  new(8, 10, 8.8, 11)),
        new("DP821A", new(60, 1, 66, 1.1),  new(8, 10, 8.8, 11)),
        // DP811 / DP811A: 단일 채널(Range2 40V/5A 기준). CH2 없음.
        new("DP811",  new(40, 5, 44, 5.5),  null),
        new("DP811A", new(40, 5, 44, 5.5),  null),
    };

    /// <summary>기본값(장비 미식별/미접속 시).</summary>
    public static readonly Dp800Model Default = Table[0]; // DP832

    /// <summary>
    /// *IDN? 응답(예: "RIGOL TECHNOLOGIES,DP832,DP8...,00.01.16")에서 모델을 식별한다.
    /// 알 수 없는 모델이면 IDN의 모델명을 이름으로 쓰되 DP832 정격을 대체 적용.
    /// </summary>
    public static Dp800Model FromIdn(string idn)
    {
        string model = ParseModel(idn);
        foreach (var m in Table)
            if (string.Equals(m.Name, model, StringComparison.OrdinalIgnoreCase))
                return m;

        // 미등록 모델: 이름만 반영하고 정격은 안전하게 DP832 값으로.
        if (!string.IsNullOrWhiteSpace(model))
            return Default with { Name = model };
        return Default;
    }

    /// <summary>*IDN? 2번째 필드(모델명) 추출.</summary>
    public static string ParseModel(string idn)
    {
        if (string.IsNullOrWhiteSpace(idn)) return "";
        var parts = idn.Split(',');
        return parts.Length >= 2 ? parts[1].Trim() : "";
    }
}
