# 모바일 개발 환경 안내

> 안드로이드 빌드와 에뮬레이터로 확인하는 절차. 실기기 없이 여기까지 검증할 수 있습니다.
> 출시 계획은 `mobile-roadmap.md`, 목표 구조는 `mobile-architecture.md`를 봅니다.

## 빠른 시작

```bash
scripts/emulator.sh start all   # 에뮬레이터 6대 기동(가로 고정). 숫자를 주면 그 대수만
scripts/emulator.sh build       # 개발 APK 빌드 (Unity 에디터가 닫혀 있어야 합니다)
scripts/emulator.sh install all # 기동 중인 기기 전부에 설치 + 실행
scripts/emulator.sh list        # 기동 상태 확인
```

6대는 게임 최대 정원과 같습니다. 대당 1.5GB를 쓰므로 6대면 9GB이고, 24GB 기기에서 Unity
에디터나 빌드와 동시에 돌리면 스왑이 걸립니다. 두세 대만 필요하면 `start 1`처럼 개별로
띄우는 편이 낫습니다.

서버는 호스트에서 따로 띄웁니다. 공개 저장소라 개발용 fallback 키가 노출돼 있으므로
반드시 키를 환경변수로 넘겨야 합니다.

```bash
cd server/BbongServer
BBONG_JWT_KEY="$(cat ~/.bbong-jwt-key)" \
BBONG_DB_CONN="Host=localhost;Port=5432;Database=bbong;Username=bbong;Password=bbong_dev" \
dotnet run --urls http://localhost:5080
```

에뮬레이터 안에서 `localhost`는 에뮬레이터 자신을 가리킵니다. 호스트는 `10.0.2.2`이고,
개발 빌드가 이 주소를 자동으로 보도록 `ServerApi`에 들어 있습니다. 배포 빌드는 운영 서버를 봅니다.

두 대만 띄워도 친구방 대전을 확인할 수 있습니다. 한쪽에서 방을 만들고 초대코드를 다른 쪽에
입력하면 됩니다. 재접속이나 이탈 시 봇 인계도 이 구성에서 재현됩니다. 6대를 띄우면 정원이
꽉 찬 상태를 볼 수 있는데, 6인 좌석 배치나 리더보드 간섭처럼 인원이 많아야 드러나는 문제를
확인할 때 씁니다.

## 설치돼 있는 것

에뮬레이터는 Unity 번들 SDK가 아니라 `~/Library/Android/sdk`에 따로 설치했습니다. Unity가
들고 있는 SDK에는 에뮬레이터와 시스템 이미지가 없기 때문입니다.

| 항목 | 값 |
|---|---|
| 시스템 이미지 | android-35, google_apis, arm64-v8a |
| 가상 기기 | bbong-1~6 (포트 5554부터 2씩 증가) |
| 대당 메모리 | 1.5GB |
| 화면 | 1080×2400, 상단 펀치홀 컷아웃 있음 |
| 패키지 | com.yogoloper.bbong |

Apple Silicon에서 arm64 이미지는 에뮬레이션이 아니라 네이티브로 돌아 실기기에 가깝습니다.
GPU는 `-gpu host`(Metal)를 씁니다. 소프트웨어 렌더러로 띄우면 첫 화면에 47초가 걸리고
`screencap`이 검은 화면만 캡처합니다.

## 알아둬야 할 함정

### 프로젝트 설정을 바꿔도 빌드에 반영되지 않습니다

`Library/Bee/Android`에 생성된 Gradle 프로젝트가 캐시로 남아 예전 설정을 그대로 씁니다.
하루에 세 번(IAP 의존성, 앱 진입 방식, 입력 핸들러) 이것 때문에 잘못된 진단을 했습니다.

```bash
rm -rf client/BbongClient/Library/Bee/Android
```

그래서 스토어에 영향을 주는 값은 `CliBuild.ApplyMobileSettings`에서 빌드 시점에 강제합니다.
패키지 ID, IL2CPP, ARM64, 앱 진입 방식, HTTP 정책이 여기 있습니다. 에디터 설정을 바꾸는 대신
이 코드를 고치는 편이 안전합니다.

### HTTP 차단이 두 겹입니다

안드로이드 9부터 평문 통신이 기본 차단이고, 여기에 더해 Unity의 `UnityWebRequest`가 별도로
막습니다. 둘 다 풀어야 개발 서버에 붙습니다. 전자는 `AndroidManifestPatcher`가, 후자는
`PlayerSettings.insecureHttpOption`이 담당하며 **개발 빌드에만** 적용됩니다.

### 뒤로가기는 플랫폼 API로 직접 받습니다

Unity 입력으로는 안드로이드 뒤로가기를 받을 수 없었습니다. Keyboard 장치는 있는데 Escape로
매핑되지 않고, 레거시 입력을 켜도 빌드에 반영되지 않았습니다. 그래서 `AppHost`가
`OnBackInvokedCallback`(Android 13+)을 직접 등록합니다. 두 가지가 전제입니다.

- 매니페스트에 `enableOnBackInvokedCallback="true"` — 없으면 콜백을 등록해도 시스템이 무시합니다
- 우선순위 `PRIORITY_OVERLAY`(1,000,000) — Unity가 기본 우선순위로 자기 콜백을 등록해
  뒤로가기를 먹기 때문에, 같은 우선순위로는 한 번도 호출되지 않습니다

### 터치에는 hover가 없습니다

마우스는 커서가 벗어나면 `PointerExit`가 오지만 터치는 손을 떼도 오지 않는 경우가 있습니다.
`CardMotion`이 이것 때문에 마지막으로 만진 카드를 확대된 채 남겨뒀습니다. 포인터 ID로
터치를 구분해 hover를 적용하지 않습니다.

### adb가 자주 offline이 됩니다

에뮬레이터를 오래 띄워두면 `adb: device offline`이 납니다. 서버를 재시작하면 복구됩니다.

```bash
adb kill-server && adb start-server && adb wait-for-device
```

## 확인할 때 참고

에뮬레이터에서만 드러나는 것들이 있습니다. 뒤로가기, 터치 조작, 세이프에어리어(이 기기에는
실제 컷아웃이 있습니다), 소프트 키보드가 가리는 영역, 앱 재시작 후 계정 유지 같은 것들입니다.
반대로 화면 레이아웃만 빠르게 보고 싶으면 Unity 에디터의 Device Simulator가 훨씬 빠릅니다.
빌드 한 번에 15~25분이 걸리므로, 확인할 항목을 모아서 한 번에 돌리는 편이 낫습니다.

에디터가 열려 있으면 CLI 빌드가 되지 않습니다. 두 방식을 동시에 쓸 수 없으니 시간을 나눠
써야 합니다.

## 아직 안 되는 것

iOS는 Xcode가 설치돼 있지 않아 확인할 수 없습니다. Apple Developer Program($99/년)이
필요한 단계라 M4 이후 과제입니다.

실제 성능과 발열, 기기별 화면비는 에뮬레이터로 알 수 없습니다. 출시 전에 실기기 확인이
한 번은 필요하고, 주변 기기를 빌리거나 Firebase Test Lab 같은 클라우드 기기 팜을 쓰는
방법이 있습니다.
