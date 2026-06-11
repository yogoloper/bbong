# client — Unity 클라이언트 (Phase 3)

Unity(C#) 앱. UI/연출·입력·로컬 AI(싱글)·네트워크 클라이언트 담당.

- Unity 버전: **6000.4.10f1**, 프로젝트 폴더: `BbongClient/`.
- 설계: `../docs/architecture.md` §1 CLIENT 참고.

## 코어 엔진 연동 (DLL 참조)

코어(`../core/BbongCore`)는 C# 10을 쓰므로 Unity가 소스를 직접 컴파일하지 않고 **빌드된 DLL을 참조**합니다.

- DLL 위치: `BbongClient/Assets/Plugins/BbongCore/BbongCore.dll` (netstandard2.1, 커밋 대상)
- **코어 수정 후 동기화**: 레포 루트에서 `./scripts/sync-core-dll.sh` 실행 → 재빌드 + 복사. Unity 열려 있으면 자동 리임포트.

## 연동 확인 (스모크 테스트)

`Assets/Scripts/EngineSmokeTest.cs` — 빈 GameObject에 붙이고 Play하면 Console에 덱 48장/딜링 결과 출력.
