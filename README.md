# RigolWidget

Windows 바탕화면에 항상 떠 있는 **RIGOL DP800 시리즈 전원공급기 제어 위젯**입니다. RIGOL 순정 PC 소프트웨어(Ultra Power)에서 그래프와 CH3를 걷어내고, 실사용에 필요한 **CH1·CH2 제어와 모니터링**만 남긴 초경량 위젯입니다. USB(VISA/SCPI)로 장비와 통신하며, 접속 시 `*IDN?`로 모델을 자동 인식해 타이틀·정격 범위를 맞춥니다.

![RigolWidget 메인 위젯](docs/screenshot.png)

## 기능

- **모델 자동 인식** — 접속 시 `*IDN?`로 모델을 읽어 타이틀 표시와 채널별 전압/전류 정격을 자동 설정 (DP832·DP831·DP821·DP811 및 A 변형)
- **실시간 측정** — CH1·CH2의 전압/전류를 7세그먼트(VFD) 디스플레이로 표시
- **CV/CC 모드 표시** — 장비의 현재 동작 모드(정전압/정전류)를 실시간 반영
- **출력 ON/OFF** — 채널별 세로 토글 스위치
- **전압/전류 설정** — 측정값 클릭(팝업) 또는 마우스 휠로 직접 조정
- **보호 기능** — OCP(과전류)·OCV(과전압, SCPI OVP) 활성화 및 임계값 설정, 트립(TRIP) 발생 시 경고 표시·해제
- **연결 자동 복구** — 운용 중 USB가 끊겨도 마지막 장비로 계속 재접속 시도
- **AI 제어 (MCP 서버 내장)** — Claude 등 MCP 클라이언트가 자연어로 전원공급기를 읽고 제어 (기본 비활성, 옵트인)
- **위젯 UX** — 항상 위 고정, 프레임리스 드래그 이동, 투명도 조절, 모니터 가장자리 자석 스냅

## 사용법

### 1. 장비 선택

프로그램을 실행하면 먼저 장비 선택 창이 뜹니다. 연결된 USB 계측기 목록에서 DP832를 고른 뒤 연결합니다. (USB 인터페이스만 지원)

![장비 선택](docs/device-select.png)

### 2. 메인 위젯 조작

| 대상 | 동작 | 방법 |
|---|---|---|
| **출력 토글** | 채널 ON/OFF | 왼쪽 세로 스위치 클릭 |
| **전압/전류 설정** | 정밀 입력 | 측정값(초록/시안 숫자) **클릭** → 팝업에서 숫자 입력·스텝 버튼·`적용` |
| | 빠른 증감 | 측정값 위에서 **마우스 휠** (전압 ±1 V / 전류 ±0.1 A) |
| | 미세 증감 | **Ctrl + 마우스 휠** (전압 ±0.1 V / 전류 ±0.01 A) |
| **OCP / OCV** | 보호 켜기·끄기 | 오른쪽 보호 그룹의 체크박스 클릭 |
| | 임계값 변경 | 옆 입력칸에 숫자 입력 후 Enter |
| **TRIP 해제** | 보호 트립 복구 | 빨갛게 깜빡이는 `TRIP` 배지 클릭 |
| **창 이동** | 위치 변경 | 타이틀바 드래그 (화면 가장자리에 자석 스냅) |
| **미니창 모드** | 전체 ↔ 미니 전환 | 타이틀바 **더블클릭** |
| **투명도** | 진하게 ↔ 투명 | 타이틀바 위에서 **마우스 휠** |
| **항상 위 고정** | TopMost 토글 | 타이틀바 📌 버튼, 또는 우클릭 메뉴 |
| **버전 확인 / 닫기** | — | 타이틀바 **우클릭** 메뉴 |

### 3. 미니창 모드

타이틀바를 **더블클릭**하면 출력 토글과 측정 전압/전류만 남긴 초소형 뷰로 전환됩니다. 다시 더블클릭하면 전체 모드로 돌아옵니다. 좁은 화면 한쪽에 상태만 띄워두기 좋습니다.

![미니창 모드](docs/screenshot-mini.png)

> **설정값(SET) 표시**: 측정값 오른쪽에 현재 설정된 전압/전류가 항상 표시됩니다. 휠로 조정하는 동안에는 숫자가 깜빡이며, 조작을 멈추고 약 2초 뒤 장비로 전송됩니다(연속 조작 중 과도한 명령 전송 방지).

### 전압/전류 vs OCP/OCV의 차이

- **전압 / 전류 설정**: 출력 레귤레이션 값입니다. 전류 설정은 "차단"이 아니라 **제한(리미트)** 으로, 부하가 이 값을 넘게 요구하면 장비가 전압을 낮춰 전류를 묶습니다(CC 모드).
- **OCP / OCV**: 보호 **차단기**입니다. 설정 임계값을 넘으면 출력을 즉시 끄고(트립), 원인 제거 후 수동 해제해야 다시 켜집니다.

## 요구 사항

- **Windows 10/11 (x64)**
- **VISA 런타임** — [NI-VISA](https://www.ni.com/visa) 등. `visa32.dll`을 통해 장비와 통신하므로 반드시 필요합니다.
- **RIGOL DP800 시리즈** (USB 연결) — 아래 [지원 모델](#지원-모델) 표 참고
- 프레임워크 의존 버전 실행 시 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)

## 지원 모델

접속 시 `*IDN?`로 모델을 인식해 타이틀 표시와 채널별 전압/전류 정격을 자동으로 맞춥니다. 정격 값은 각 모델의 데이터시트를 기준으로 내장돼 있습니다.

| 모델 | 제어 채널 | 정격 (CH1 / CH2) | 지원 수준 | 실제 테스트 |
|---|---|---|:---:|:---:|
| **DP832 / DP832A** | CH1, CH2 | 30V·3A / 30V·3A | ✅ 완전 지원 | ✅ **DP832 검증 완료** |
| **DP831 / DP831A** | CH1, CH2 | 8V·5A / 30V·2A | ✅ 정격 자동 적용 | ⬜ 미검증 |
| **DP821 / DP821A** | CH1, CH2 | 60V·1A / 8V·10A | ✅ 정격 자동 적용 | ⬜ 미검증 |
| **DP811 / DP811A** | CH1 (단일 채널) | 40V·5A / — | 🟡 단일 채널로 표시 | ⬜ 미검증 |
| DP700 (DP711/DP712) | — | — | ❌ 미지원 | — |

- ✅ **실제 테스트**: 현재 **DP832**만 하드웨어로 검증했습니다. 나머지 DP800 모델은 SCPI 명령셋이 동일해 동작이 예상되며 정격은 자동 적용되지만, 실물 검증은 아직입니다. 사용해 보시고 이상이 있으면 [Issue](https://github.com/firepooh/RigolWidget/issues)로 알려주세요.
- 🟡 **DP811(A)**: 단일 채널 모델이라 CH1만 표시됩니다(CH2 숨김).
- ❌ **DP700 시리즈**: RS232 기반의 다른 명령셋을 사용해 지원하지 않습니다.

## AI 제어 (내장 MCP 서버)

위젯에 [MCP(Model Context Protocol)](https://modelcontextprotocol.io) 서버가 내장되어 있어, Claude 같은 AI 클라이언트가 자연어로 전원공급기를 읽고 제어할 수 있습니다. *"CH1을 3.3V로 설정해줘"*, *"지금 전류 얼마야?"*, *"전부 출력 꺼줘"* 처럼요.

**실행 중인 위젯과 같은 USB 세션을 공유**하므로 세션 충돌이 없고, AI가 바꾼 값은 위젯 화면에도 곧바로 반영됩니다.

### 켜는 법

1. 위젯 타이틀바 **우클릭 → "MCP 서버"** 체크 → 로컬 서버가 `http://127.0.0.1:7735/` 에서 시작됩니다.
2. AI가 출력·전압을 **변경**하게 하려면 **"MCP 제어 허용"** 도 켜세요. (꺼두면 읽기 전용 — 조회만 가능)
3. "MCP 주소 복사"로 엔드포인트 URL을 복사해 MCP 클라이언트에 등록합니다.

예) Claude Code에 등록:
```
claude mcp add --transport http rigol http://127.0.0.1:7735/
```

### 안전장치

- **기본 비활성**: MCP 서버도, 제어 허용도 모두 꺼진 상태로 시작합니다(명시적 옵트인).
- **로컬 전용**: `127.0.0.1` 에만 바인딩되어 외부에서 접근할 수 없습니다.
- **정격 클램프**: 모든 설정값은 감지된 모델 정격 범위로 제한됩니다.
- **로그**: MCP가 보낸 모든 쓰기 명령은 `%LOCALAPPDATA%\RigolWidget\rigolwidget.log` 에 기록됩니다.

### 제공 도구(tool)

| 도구 | 설명 |
|---|---|
| `get_status` | 전 채널 측정값·설정값·출력·CV/CC·OCP/OVP·트립 상태 조회 |
| `get_identity` | 모델명·IDN 조회 |
| `set_voltage` / `set_current` | 채널 전압 / 전류 리미트 설정 |
| `set_output` | 채널 출력 ON/OFF |
| `set_ocp` / `set_ovp` | 과전류 / 과전압 보호 활성·임계값 설정 |
| `clear_trip` | 보호 트립(OCP/OVP) 해제 |

> 조회 도구(`get_*`)는 항상 동작하고, 나머지 **제어 도구는 "MCP 제어 허용"이 켜져 있을 때만** 실행됩니다(꺼져 있으면 거부).

## 다운로드

[Releases](https://github.com/firepooh/RigolWidget/releases)에서 받을 수 있습니다:

| 파일 | 크기 | 조건 |
|---|---|---|
| `RigolWidget-standalone.exe` | ~80MB | .NET 런타임 포함 (VISA 런타임만 있으면 실행) |
| `RigolWidget.exe` | ~4MB | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) + [ASP.NET Core 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) 필요 |

두 버전 모두 별도로 **VISA 런타임(NI-VISA 등)** 설치가 필요합니다. (경량 버전은 내장 MCP 서버 때문에 ASP.NET Core 런타임도 필요하며, standalone 버전엔 모두 포함되어 있습니다.)

## 빌드

```
dotnet build -c Release
```

Visual Studio 2022에서 `RigolWidget.csproj`를 바로 열어도 됩니다.
실행 파일: `bin\Release\net8.0-windows\win-x64\RigolWidget.exe`

## 릴리스 방법

버전 태그를 푸시하면 [GitHub Actions](.github/workflows/build.yml)가 자동으로 두 종류(프레임워크 의존 / self-contained)의 단일 exe를 빌드해 Release에 첨부합니다.

```
git tag v1.0.0
git push origin v1.0.0
```

## 동작 원리

- **장비 통신**: `visa32.dll`(VISA C API)을 직접 P/Invoke하여 SCPI 명령을 주고받습니다. 별도 NuGet/COM 참조 없이 런타임에 시스템의 VISA 구현을 로드합니다.
- **폴링**: 약 1초 주기로 측정값(`:MEAS:ALL?`)과 동작 모드(`:OUTP:MODE?`)를 읽고, 4주기마다 설정값·보호 상태·트립 알람까지 전체 동기화합니다. 묶음 질의(`:MEAS:ALL?`, `:APPL?`)로 USB 호출 수를 최소화했습니다.
- **연결 복구**: 통신 오류 발생 시 세션을 닫고 마지막 리소스로 주기적으로 재접속을 시도합니다. 통신 실패는 `%LOCALAPPDATA%\RigolWidget\rigolwidget.log`에 기록됩니다.
- **7세그먼트 표시**: [DSEG7 Classic](https://github.com/keshikan/DSEG) 폰트(SIL OFL)를 앱에 임베드하여 실행 PC에 폰트 설치 없이 VFD 스타일로 렌더링합니다.
- **렌더링 최적화**: 그림자를 내용과 분리하고, 값이 바뀔 때만 텍스트를 갱신하며, 상시 애니메이션은 저프레임으로 제한해 유휴 시 CPU/GPU 부하를 최소화했습니다.

## 주요 SCPI 명령 (DP832)

```
:MEAS:ALL? CHn              # 실측 전압·전류·전력 (묶음)
:OUTP:MODE? CHn             # 동작 모드 (CV / CC / UR)
:APPL? CHn                  # 설정 전압·전류 (묶음)
:SOURn:VOLT <v>            # 전압 설정      / :SOURn:VOLT?
:SOURn:CURR <a>            # 전류 리미트    / :SOURn:CURR?
:OUTP CHn,ON|OFF           # 출력 on/off    / :OUTP? CHn
:OUTP:OCP CHn,ON|OFF       # OCP on/off     / :OUTP:OCP:VAL CHn,<a> / :OUTP:OCP:ALAR? CHn / :OUTP:OCP:CLEAR CHn
:OUTP:OVP CHn,ON|OFF       # OVP(OCV) on/off / :OUTP:OVP:VAL CHn,<v> / :OUTP:OVP:ALAR? CHn / :OUTP:OVP:CLEAR CHn
```

## 라이선스

[MIT](LICENSE)

7세그먼트 폰트 [DSEG](https://github.com/keshikan/DSEG)는 SIL Open Font License 1.1을 따릅니다.
