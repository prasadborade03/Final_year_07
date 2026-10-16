using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using ProjectName.Rover;
using ProjectName.Planetary;
using ProjectName.Terrain;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ProjectName.Output
{
    public class OutputPanelController
    {
        private readonly CultureInfo inv = CultureInfo.InvariantCulture;

        // UI Toolkit Elements
        private VisualElement root;
        private Button btnModeOutput;
        private Button recChip;
        private VisualElement recChipDot;
        private Label lblRecChip;

        private VisualElement outputRoot;
        private Label lblRecState;
        private VisualElement recStateDot;
        private Label lblRecElapsed;
        private Label lblRecSamples;
        private Label lblRecSize;

        private Button btnRate5;
        private Button btnRate10;
        private Button btnRate20;
        private Toggle tglAutoStart;
        private Toggle tglWheelData;

        private VisualElement recBtnContainer;
        private Button btnRecStart;
        private VisualElement recActiveBtns;
        private Button btnRecPause;
        private Button btnRecResume;
        private Button btnRecStop;
        private Label lblRecHint;

        private VisualElement traceView;
        private VisualElement traceChannelRow;
        private Label lblTraceEmpty;
        private LiveTraceElement liveTrace;
        private Button[] channelChips = new Button[6];

        private VisualElement summaryGrid;
        private Label sumDist, sumAvgV, sumMaxV, sumEnergy, sumWhPerM, sumSlip;
        private Label sumSlope, sumPitch, sumRoll, sumG, sumBatt, sumWarn;
        private Label lblOutputStatus;

        private ScrollView sessionList;
        private Label lblSessionsEmpty;
        private Label lblSelName;
        private Label lblSelMeta;
        private Label lblSelPath;
        private Button btnOpenFolder;
        private Button btnCopyPath;
        private Button btnExportZip;
        private Button btnDeleteSession;
        private Button btnExportAll;

        private List<SessionEntry> loadedSessions = new List<SessionEntry>();
        private SessionEntry selectedSession = null;
        private VisualElement selectedRowElement = null;

        private bool isOutputVisible = false;
        private bool recDotPulseState = false;

        public OutputPanelController(VisualElement rootVisualElement)
        {
            this.root = rootVisualElement;
            BindElements();
            InitEvents();
            RefreshSessionList();
            UpdateRecorderStateUI(MissionRecorder.Instance != null ? MissionRecorder.Instance.State : RecordingState.Idle);

            // Pulse timer for rec chip dot
            root.schedule.Execute(PulseRecDot).Every(500);

            // Periodic live update timer (100 ms = 10 Hz)
            root.schedule.Execute(PeriodicUpdate).Every(100);
        }

        private void BindElements()
        {
            if (root == null) return;

            btnModeOutput = root.Q<Button>("BtnModeOutput");
            recChip = root.Q<Button>("RecChip");
            recChipDot = root.Q<VisualElement>("RecChipDot");
            lblRecChip = root.Q<Label>("LblRecChip");

            outputRoot = root.Q<VisualElement>("OutputRoot");
            lblRecState = root.Q<Label>("LblRecState");
            recStateDot = root.Q<VisualElement>("RecStateDot");
            lblRecElapsed = root.Q<Label>("LblRecElapsed");
            lblRecSamples = root.Q<Label>("LblRecSamples");
            lblRecSize = root.Q<Label>("LblRecSize");

            btnRate5 = root.Q<Button>("BtnRate5");
            btnRate10 = root.Q<Button>("BtnRate10");
            btnRate20 = root.Q<Button>("BtnRate20");
            tglAutoStart = root.Q<Toggle>("TglAutoStart");
            tglWheelData = root.Q<Toggle>("TglWheelData");

            btnRecStart = root.Q<Button>("BtnRecStart");
            recActiveBtns = root.Q<VisualElement>("output-rec-active-btns") ?? btnRecStart?.parent?.Q<VisualElement>(className: "output-rec-active-btns");
            btnRecPause = root.Q<Button>("BtnRecPause");
            btnRecResume = root.Q<Button>("BtnRecResume");
            btnRecStop = root.Q<Button>("BtnRecStop");
            lblRecHint = root.Q<Label>("LblRecHint");

            traceView = root.Q<VisualElement>("TraceView");
            traceChannelRow = root.Q<VisualElement>("TraceChannelRow");
            lblTraceEmpty = root.Q<Label>("LblTraceEmpty");

            // Summary cells
            summaryGrid = root.Q<VisualElement>("SummaryGrid");
            sumDist = root.Q<Label>("SumDist");
            sumAvgV = root.Q<Label>("SumAvgV");
            sumMaxV = root.Q<Label>("SumMaxV");
            sumEnergy = root.Q<Label>("SumEnergy");
            sumWhPerM = root.Q<Label>("SumWhPerM");
            sumSlip = root.Q<Label>("SumSlip");
            sumSlope = root.Q<Label>("SumSlope");
            sumPitch = root.Q<Label>("SumPitch");
            sumRoll = root.Q<Label>("SumRoll");
            sumG = root.Q<Label>("SumG");
            sumBatt = root.Q<Label>("SumBatt");
            sumWarn = root.Q<Label>("SumWarn");

            lblOutputStatus = root.Q<Label>("LblOutputStatus");

            sessionList = root.Q<ScrollView>("SessionList");
            lblSessionsEmpty = root.Q<Label>("LblSessionsEmpty");
            lblSelName = root.Q<Label>("LblSelName");
            lblSelMeta = root.Q<Label>("LblSelMeta");
            lblSelPath = root.Q<Label>("LblSelPath");
            btnOpenFolder = root.Q<Button>("BtnOpenFolder");
            btnCopyPath = root.Q<Button>("BtnCopyPath");
            btnExportZip = root.Q<Button>("BtnExportZip");
            btnDeleteSession = root.Q<Button>("BtnDeleteSession");
            btnExportAll = root.Q<Button>("BtnExportAll");

            // Attach LiveTraceElement into TraceView
            if (traceView != null)
            {
                liveTrace = new LiveTraceElement();
                traceView.Add(liveTrace);
            }

            // Channel chips
            if (traceChannelRow != null)
            {
                channelChips[0] = traceChannelRow.Q<Button>("ChipSpeed");
                channelChips[1] = traceChannelRow.Q<Button>("ChipSlope");
                channelChips[2] = traceChannelRow.Q<Button>("ChipSlip");
                channelChips[3] = traceChannelRow.Q<Button>("ChipPower");
                channelChips[4] = traceChannelRow.Q<Button>("ChipBattery");
                channelChips[5] = traceChannelRow.Q<Button>("ChipTemp");

                for (int i = 0; i < 6; i++)
                {
                    int chIndex = i;
                    if (channelChips[chIndex] != null)
                    {
                        channelChips[chIndex].clicked += () => ToggleChannel(chIndex);
                    }
                }
            }

            // Hide open folder button on mobile / Quest platforms
#if !UNITY_STANDALONE && !UNITY_EDITOR
            if (btnOpenFolder != null) btnOpenFolder.style.display = DisplayStyle.None;
#endif
        }

        private void InitEvents()
        {
            var rec = MissionRecorder.Instance;
            if (rec != null)
            {
                rec.OnStateChanged += UpdateRecorderStateUI;
                rec.OnStatusMessage += SetStatus;
                rec.OnSessionSaved += OnSessionSavedHandler;
            }

            if (recChip != null)
            {
                recChip.clicked += () =>
                {
                    UploadHeightmapUIToolkit.Instance?.SetViewMode(UploadHeightmapUIToolkit.StudioViewMode.Output);
                };
            }

            if (btnModeOutput != null)
            {
                btnModeOutput.clicked += () =>
                {
                    UploadHeightmapUIToolkit.Instance?.SetViewMode(UploadHeightmapUIToolkit.StudioViewMode.Output);
                };
            }

            // Rate Buttons
            if (btnRate5 != null) btnRate5.clicked += () => SetRate(5);
            if (btnRate10 != null) btnRate10.clicked += () => SetRate(10);
            if (btnRate20 != null) btnRate20.clicked += () => SetRate(20);

            // Toggles
            if (tglAutoStart != null)
            {
                tglAutoStart.value = rec != null && rec.AutoStartOnFirstMovement;
                tglAutoStart.RegisterValueChangedCallback(evt =>
                {
                    if (MissionRecorder.Instance != null)
                    {
                        MissionRecorder.Instance.AutoStartOnFirstMovement = evt.newValue;
                    }
                });
            }

            if (tglWheelData != null)
            {
                tglWheelData.value = rec == null || rec.IncludeWheelData;
                tglWheelData.RegisterValueChangedCallback(evt =>
                {
                    if (MissionRecorder.Instance != null)
                    {
                        MissionRecorder.Instance.IncludeWheelData = evt.newValue;
                    }
                });
            }

            // Action Buttons
            if (btnRecStart != null)
            {
                btnRecStart.clicked += () =>
                {
                    if (MissionRecorder.Instance == null) return;
                    if (!MissionRecorder.Instance.CanStartRecording(out string hint))
                    {
                        SetHint(hint);
                        return;
                    }
                    SetHint("");
                    bool ok = MissionRecorder.Instance.StartRecording(out string err);
                    if (!ok) SetStatus($"Error: {err}");
                };
            }

            if (btnRecPause != null)
            {
                btnRecPause.clicked += () => MissionRecorder.Instance?.Pause();
            }

            if (btnRecResume != null)
            {
                btnRecResume.clicked += () => MissionRecorder.Instance?.Resume();
            }

            if (btnRecStop != null)
            {
                btnRecStop.clicked += () => MissionRecorder.Instance?.StopAndSave();
            }

            // Export & Session Management
            if (btnOpenFolder != null)
            {
                btnOpenFolder.clicked += () =>
                {
                    if (selectedSession == null || !Directory.Exists(selectedSession.folderPath))
                    {
                        SetStatus("No session selected.");
                        return;
                    }
#if UNITY_EDITOR
                    EditorUtility.RevealInFinder(selectedSession.folderPath);
#elif UNITY_STANDALONE
                    Application.OpenURL("file://" + selectedSession.folderPath);
#endif
                };
            }

            if (btnCopyPath != null)
            {
                btnCopyPath.clicked += () =>
                {
                    if (selectedSession == null) return;
                    GUIUtility.systemCopyBuffer = selectedSession.folderPath;
                    SetStatus("Path copied to clipboard.");
                };
            }

            if (btnExportZip != null)
            {
                btnExportZip.clicked += () =>
                {
                    if (selectedSession == null)
                    {
                        SetStatus("Select a session to export.");
                        return;
                    }
                    string zip = SessionIndex.ExportZip(selectedSession);
                    if (!string.IsNullOrEmpty(zip))
                    {
                        SetStatus($"Exported: {Path.GetFileName(zip)}");
                    }
                    else
                    {
                        SetStatus("Export zip failed.");
                    }
                };
            }

            if (btnDeleteSession != null)
            {
                btnDeleteSession.clicked += () =>
                {
                    if (selectedSession == null) return;
                    string name = selectedSession.sessionId;
                    UploadHeightmapUIToolkit.Instance?.ShowConfirmationModal(
                        "DELETE SESSION?",
                        $"Permanently delete session '{name}' and all logged data?",
                        () =>
                        {
                            bool deleted = SessionIndex.DeleteSession(selectedSession);
                            if (deleted)
                            {
                                SetStatus($"Deleted session: {name}");
                                selectedSession = null;
                                RefreshSessionList();
                            }
                            else
                            {
                                SetStatus($"Failed to delete session: {name}");
                            }
                        });
                };
            }

            if (btnExportAll != null)
            {
                btnExportAll.clicked += () =>
                {
                    if (loadedSessions == null || loadedSessions.Count == 0)
                    {
                        SetStatus("No sessions available to export.");
                        return;
                    }
                    string zip = SessionIndex.ExportAll(loadedSessions);
                    if (!string.IsNullOrEmpty(zip))
                    {
                        SetStatus($"Exported all sessions: {Path.GetFileName(zip)}");
                    }
                    else
                    {
                        SetStatus("Export all failed.");
                    }
                };
            }
        }

        public void OnViewModeChanged(UploadHeightmapUIToolkit.StudioViewMode mode)
        {
            isOutputVisible = (mode == UploadHeightmapUIToolkit.StudioViewMode.Output);
            if (isOutputVisible)
            {
                RefreshSessionList();
                liveTrace?.RefreshTrace();
            }
        }

        private void SetRate(int hz)
        {
            if (MissionRecorder.Instance == null) return;
            if (MissionRecorder.Instance.State != RecordingState.Idle) return;

            MissionRecorder.Instance.SetSampleRate(hz);
            btnRate5?.RemoveFromClassList("segmented-active");
            btnRate10?.RemoveFromClassList("segmented-active");
            btnRate20?.RemoveFromClassList("segmented-active");

            if (hz == 5) btnRate5?.AddToClassList("segmented-active");
            else if (hz == 20) btnRate20?.AddToClassList("segmented-active");
            else btnRate10?.AddToClassList("segmented-active");
        }

        private void ToggleChannel(int index)
        {
            if (liveTrace == null || index < 0 || index >= liveTrace.ChannelEnabled.Length) return;
            bool nextState = !liveTrace.ChannelEnabled[index];
            liveTrace.SetChannelEnabled(index, nextState);

            if (channelChips[index] != null)
            {
                if (nextState) channelChips[index].AddToClassList("material-chip-active");
                else channelChips[index].RemoveFromClassList("material-chip-active");
            }
        }

        private void PulseRecDot()
        {
            var rec = MissionRecorder.Instance;
            if (rec == null) return;

            bool active = rec.State == RecordingState.Recording || rec.State == RecordingState.Paused;
            if (recChip != null)
            {
                recChip.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (active)
            {
                recDotPulseState = !recDotPulseState;
                if (recDotPulseState && rec.State == RecordingState.Recording)
                {
                    recChipDot?.AddToClassList("rec-chip-dot-pulse");
                }
                else
                {
                    recChipDot?.RemoveFromClassList("rec-chip-dot-pulse");
                }

                TimeSpan t = TimeSpan.FromSeconds(rec.SimTime);
                string timeStr = $"{t.Minutes:D2}:{t.Seconds:D2}";
                string prefix = rec.State == RecordingState.Recording ? "REC" : "PAUSED";
                if (lblRecChip != null) lblRecChip.text = $"{prefix} {timeStr}";
            }
        }

        private void PeriodicUpdate()
        {
            var rec = MissionRecorder.Instance;
            if (rec == null) return;

            // Start button validation hint
            if (rec.State == RecordingState.Idle)
            {
                bool canStart = rec.CanStartRecording(out string reason);
                if (btnRecStart != null)
                {
                    if (canStart) btnRecStart.RemoveFromClassList("btn-disabled");
                    else btnRecStart.AddToClassList("btn-disabled");
                }
                SetHint(canStart ? "" : reason);
            }

            if (rec.State == RecordingState.Recording || rec.State == RecordingState.Paused)
            {
                TimeSpan t = TimeSpan.FromSeconds(rec.SimTime);
                if (lblRecElapsed != null) lblRecElapsed.text = $"{t.Hours:D2}:{t.Minutes:D2}:{t.Seconds:D2}";
                if (lblRecSamples != null) lblRecSamples.text = rec.SampleCount.ToString();
                if (lblRecSize != null) lblRecSize.text = FormatBytes(rec.BytesWritten);

                // Live running summary update
                var liveSummary = rec.Stats.ComputeSummary(rec.SimTime);
                DisplaySummary(liveSummary);
            }

            if (isOutputVisible)
            {
                liveTrace?.RefreshTrace();
            }
        }

        private void UpdateRecorderStateUI(RecordingState state)
        {
            if (lblRecState != null) lblRecState.text = state.ToString().ToUpper();

            if (recStateDot != null)
            {
                recStateDot.RemoveFromClassList("tint-good");
                recStateDot.RemoveFromClassList("tint-fair");
                recStateDot.RemoveFromClassList("tint-poor");

                switch (state)
                {
                    case RecordingState.Idle:
                        break;
                    case RecordingState.Recording:
                        recStateDot.AddToClassList("tint-poor");
                        break;
                    case RecordingState.Paused:
                        recStateDot.AddToClassList("tint-fair");
                        break;
                }
            }

            // Buttons visibility
            if (btnRecStart != null)
            {
                btnRecStart.style.display = (state == RecordingState.Idle) ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (recActiveBtns != null)
            {
                recActiveBtns.style.display = (state != RecordingState.Idle) ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (btnRecPause != null)
            {
                btnRecPause.style.display = (state == RecordingState.Recording) ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (btnRecResume != null)
            {
                btnRecResume.style.display = (state == RecordingState.Paused) ? DisplayStyle.Flex : DisplayStyle.None;
            }

            // Rate buttons interactable only when Idle
            bool rateInteractable = (state == RecordingState.Idle);
            btnRate5?.SetEnabled(rateInteractable);
            btnRate10?.SetEnabled(rateInteractable);
            btnRate20?.SetEnabled(rateInteractable);

            // Toggles disabled during recording
            tglAutoStart?.SetEnabled(state == RecordingState.Idle);
            tglWheelData?.SetEnabled(state == RecordingState.Idle);

            if (state == RecordingState.Idle)
            {
                if (lblRecElapsed != null) lblRecElapsed.text = "00:00:00";
                if (lblRecSamples != null) lblRecSamples.text = "0";
                if (lblRecSize != null) lblRecSize.text = "0 KB";

                if (selectedSession != null)
                {
                    DisplaySummary(selectedSession.summary);
                }
                else
                {
                    ClearSummary();
                }
            }
        }

        private void OnSessionSavedHandler(string dir)
        {
            RefreshSessionList();
            if (loadedSessions.Count > 0)
            {
                SelectSession(loadedSessions[0]);
            }
        }

        public void RefreshSessionList()
        {
            if (sessionList == null) return;
            sessionList.Clear();

            loadedSessions = SessionIndex.ScanSessions();
            if (lblSessionsEmpty != null)
            {
                lblSessionsEmpty.style.display = (loadedSessions.Count == 0) ? DisplayStyle.Flex : DisplayStyle.None;
            }

            SessionEntry toSelect = selectedSession;
            bool foundSelected = false;

            foreach (var entry in loadedSessions)
            {
                var row = CreateSessionRow(entry);
                sessionList.Add(row);

                if (toSelect != null && entry.folderPath == toSelect.folderPath)
                {
                    SelectSession(entry, row);
                    foundSelected = true;
                }
            }

            if (!foundSelected && loadedSessions.Count > 0)
            {
                SelectSession(loadedSessions[0]);
            }
            else if (loadedSessions.Count == 0)
            {
                SelectSession(null);
            }
        }

        private VisualElement CreateSessionRow(SessionEntry entry)
        {
            var row = new VisualElement();
            row.AddToClassList("list-row");
            row.AddToClassList("session-list-row");

            var left = new VisualElement();
            left.style.flexDirection = FlexDirection.Column;
            left.style.flexGrow = 1;

            var titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.alignItems = Align.Center;

            var lblTitle = new Label($"{entry.roverDisplayName} · {entry.planetName}");
            lblTitle.AddToClassList("session-row-title");
            titleRow.Add(lblTitle);

            if (entry.isIncomplete)
            {
                var badge = new Label("INCOMPLETE");
                badge.AddToClassList("status-badge");
                badge.AddToClassList("session-incomplete-badge");
                badge.AddToClassList("tint-poor");
                titleRow.Add(badge);
            }
            left.Add(titleRow);

            TimeSpan d = TimeSpan.FromSeconds(entry.durationSimS);
            string sub = $"{d.Minutes:D2}:{d.Seconds:D2} · {FormatBytes(entry.totalSizeBytes)} · {entry.startedUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
            var lblSub = new Label(sub);
            lblSub.AddToClassList("session-row-sub");
            left.Add(lblSub);

            row.Add(left);

            row.RegisterCallback<ClickEvent>(evt =>
            {
                SelectSession(entry, row);
            });

            return row;
        }

        private void SelectSession(SessionEntry entry, VisualElement row = null)
        {
            selectedSession = entry;

            // Highlight row
            if (selectedRowElement != null)
            {
                selectedRowElement.RemoveFromClassList("list-row-active");
            }
            if (row != null)
            {
                row.AddToClassList("list-row-active");
                selectedRowElement = row;
            }

            if (entry == null)
            {
                if (lblSelName != null) lblSelName.text = "Select a session";
                if (lblSelMeta != null) lblSelMeta.text = "—";
                if (lblSelPath != null) lblSelPath.text = "";
                ClearSummary();
                return;
            }

            if (lblSelName != null) lblSelName.text = entry.sessionId;
            TimeSpan d = TimeSpan.FromSeconds(entry.durationSimS);
            if (lblSelMeta != null)
            {
                lblSelMeta.text = $"{entry.roverDisplayName} · {entry.planetName} · {d.Minutes:D2}:{d.Seconds:D2} · {entry.totalSamples} samples · {FormatBytes(entry.totalSizeBytes)}";
            }
            if (lblSelPath != null) lblSelPath.text = entry.folderPath;

            var rec = MissionRecorder.Instance;
            if (rec == null || rec.State == RecordingState.Idle)
            {
                DisplaySummary(entry.summary);
            }
        }

        private void DisplaySummary(SessionSummary s)
        {
            if (s == null)
            {
                ClearSummary();
                return;
            }

            if (sumDist != null) sumDist.text = s.distance_m.ToString("F2", inv);
            if (sumAvgV != null) sumAvgV.text = s.avg_speed_mps.ToString("F2", inv);
            if (sumMaxV != null) sumMaxV.text = s.max_speed_mps.ToString("F2", inv);
            if (sumEnergy != null) sumEnergy.text = s.energy_wh.ToString("F1", inv);
            if (sumWhPerM != null) sumWhPerM.text = s.energy_per_m_wh.HasValue ? s.energy_per_m_wh.Value.ToString("F3", inv) : "—";
            if (sumSlip != null) sumSlip.text = s.avg_slip_pct.ToString("F1", inv);
            if (sumSlope != null) sumSlope.text = s.max_slope_deg.ToString("F1", inv);
            if (sumPitch != null) sumPitch.text = s.max_abs_pitch_deg.ToString("F1", inv);
            if (sumRoll != null) sumRoll.text = s.max_abs_roll_deg.ToString("F1", inv);
            if (sumG != null) sumG.text = s.max_g.ToString("F2", inv);
            if (sumBatt != null) sumBatt.text = s.battery_end_pct.ToString("F1", inv);
            if (sumWarn != null) sumWarn.text = (s.rollover_warning_count + s.steep_slope_warning_count).ToString();
        }

        private void ClearSummary()
        {
            if (sumDist != null) sumDist.text = "—";
            if (sumAvgV != null) sumAvgV.text = "—";
            if (sumMaxV != null) sumMaxV.text = "—";
            if (sumEnergy != null) sumEnergy.text = "—";
            if (sumWhPerM != null) sumWhPerM.text = "—";
            if (sumSlip != null) sumSlip.text = "—";
            if (sumSlope != null) sumSlope.text = "—";
            if (sumPitch != null) sumPitch.text = "—";
            if (sumRoll != null) sumRoll.text = "—";
            if (sumG != null) sumG.text = "—";
            if (sumBatt != null) sumBatt.text = "—";
            if (sumWarn != null) sumWarn.text = "—";
        }

        private void SetHint(string hint)
        {
            if (lblRecHint != null) lblRecHint.text = hint ?? "";
        }

        private void SetStatus(string msg)
        {
            if (lblOutputStatus != null) lblOutputStatus.text = msg ?? "";
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{(bytes / 1024f):F1} KB";
            return $"{(bytes / (1024f * 1024f)):F2} MB";
        }
    }
}
