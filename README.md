# MouseGesture

Windows에서 동작하는 마우스 제스처 유틸리티.
오른쪽 버튼을 누른 채 4방향(상·하·좌·우)으로 마우스를 움직여 단축키 매크로를 실행합니다.

## 주요 기능
- 전역 저수준 마우스 후크 기반의 제스처 인식 (전용 백그라운드 스레드)
- **휠 증폭**: 지정한 버튼(기본 '뒤로' X1)을 누른 채 휠을 굴리면 한 칸당 설정한 배수만큼 스크롤. 버튼만 짧게 누르면 원래 동작(뒤로 가기 등)이 그대로 전달됨
- **관리자 권한으로 실행**: 관리자 권한 창(예: 관리자 권한 Windows Terminal) 위에서도 제스처가 가로채지도록 앱이 관리자 권한으로 동작
- 트레이 아이콘에서 설정 열기 / 일시정지 / 자동 실행 / 정보 / 종료
- 단일 인스턴스 보장 (두 번째 실행 시 기존 인스턴스의 설정 창을 띄움)
- 매핑·트리거·휠 설정은 `%APPDATA%\MouseGesture\bindings.json` 에 저장
- 로그는 `%LOCALAPPDATA%\MouseGesture\logs\` 에 일자별로 기록 (14일 후 자동 삭제)

## 시스템 요구사항
- Windows 10 / 11 (x64)
- 관리자 권한 (실행 시 UAC 승인 필요)
- (개발 시) .NET SDK 10

## 권한 / 자동 실행
- 앱은 `app.manifest` 의 `requireAdministrator` 설정으로 항상 관리자 권한으로 실행됩니다. 이는 관리자 권한으로 실행 중인 다른 프로그램(터미널 등) 위에서도 제스처가 동작하도록 하기 위함입니다(UIPI 제약 우회).
- "자동 실행"을 켜면 HKCU `Run` 키 대신 **작업 스케줄러**에 '가장 높은 권한으로 실행' 작업(`MouseGesture`)을 등록합니다. 로그인 시 UAC 창 없이 관리자 권한으로 조용히 시작됩니다. (기존 `Run` 키 항목은 자동 정리)

## 사용법
1. 설치 후 트레이 아이콘이 나타나면 우클릭 메뉴에서 "설정 열기" 선택.
2. 스트로크 입력란에 `URD` 같이 4방향 문자(`U`, `R`, `D`, `L`)를 조합해 입력하고 동작을 골라 추가.
3. 본인이 사용할 화면 어디서든 마우스 우클릭을 누른 채 해당 패턴으로 움직이면 매핑된 동작 실행.
4. 짧은 우클릭(움직이지 않음)은 그대로 컨텍스트 메뉴를 띄웁니다.
5. "휠 증폭" 카드에서 사용 여부 · 누를 버튼 · 스크롤 배수(1~20)를 설정. 예: '뒤로' 버튼을 누른 채 휠을 굴리면 3배 스크롤.

## 빌드
```powershell
dotnet build
dotnet test
```

## 배포 패키징
```powershell
# 아이콘 (재)생성
.\scripts\build-icon.ps1

# 단일 실행파일(self-contained, R2R) 발행 + 포터블 zip + (선택) 인스톨러
.\scripts\publish.ps1
```

산출물:
- `dist\MouseGesture-<버전>-portable.zip` — 압축 해제 후 즉시 실행 가능
- `dist\MouseGestureSetup-<버전>.exe` — Inno Setup이 설치돼 있을 때만 생성

Inno Setup이 없으면 [jrsoftware.org/isdl.php](https://jrsoftware.org/isdl.php)에서 6.x 설치 후 `publish.ps1` 재실행.

## 프로젝트 구조
- `src/MouseGesture.Core` — 후크/인식/디스패치/매핑(저장 포함). UI 의존성 없음.
- `src/MouseGesture.App` — Avalonia UI(트레이, 설정 창, About).
- `tests/MouseGesture.Core.Tests` — 코어 단위 테스트(MSTest).
- `scripts/` — 아이콘 생성, 발행, Inno Setup 스크립트.

## 라이선스
[MIT](LICENSE)
