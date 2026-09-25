import React, { useState, useEffect, useRef } from 'react';
import * as signalR from '@microsoft/signalr';
import { 
  Send, 
  Bot, 
  User, 
  Trash2, 
  CheckCircle2, 
  AlertCircle, 
  FileText, 
  Download, 
  RefreshCw, 
  Activity, 
  Sliders, 
  TrendingUp, 
  AlertTriangle, 
  Clock, 
  CheckSquare, 
  Square,
  Sparkles,
  BarChart2,
  Calendar,
  Layers,
  ChevronRight,
  Database,
  ListOrdered,
  Server,
  Zap,
  Wifi
} from 'lucide-react';
import './App.css';

const API_BASE_URL = 'http://localhost:5000/api';

const SIGNAL_COLORS = ['#4f46e5', '#0284c7', '#059669', '#d97706', '#dc2626', '#8b5cf6', '#ec4899', '#14b8a6'];

function App() {
  // Report State
  const [assets, setAssets] = useState([]);
  const [selectedAssetId, setSelectedAssetId] = useState('');
  const [signals, setSignals] = useState([]);
  const [selectedSignalIds, setSelectedSignalIds] = useState([]);
  const [timeRange, setTimeRange] = useState('7d');
  const [includeEvents, setIncludeEvents] = useState(true);
  const [includeAlerts, setIncludeAlerts] = useState(true);
  const [includeInsights, setIncludeInsights] = useState(true);
  const [includeCharts, setIncludeCharts] = useState(true);
  const [includeFullRawData, setIncludeFullRawData] = useState(true);

  const [reportData, setReportData] = useState(null);
  const [isGenerating, setIsGenerating] = useState(false);
  const [reportError, setReportError] = useState(null);
  const [jobs, setJobs] = useState([]);
  const [isQueueing, setIsQueueing] = useState(false);

  // Chat State
  const [messages, setMessages] = useState([]);
  const [chatInput, setChatInput] = useState('');
  const [sessionId, setSessionId] = useState('');
  const [isChatLoading, setIsChatLoading] = useState(false);
  const [chatError, setChatError] = useState(null);
  const [wsConnected, setWsConnected] = useState(false);

  const chatEndRef = useRef(null);
  const hubConnectionRef = useRef(null);

  // 1. Initial Load: Fetch Assets & set Session
  useEffect(() => {
    let existingSession = localStorage.getItem('sensor_bot_session');
    if (!existingSession) {
      existingSession = 'sess-' + Math.random().toString(36).substring(2, 9);
      localStorage.setItem('sensor_bot_session', existingSession);
    }
    setSessionId(existingSession);

    fetchAssets();
  }, []);

  // 2. Setup SignalR WebSockets Connection
  useEffect(() => {
    if (!sessionId) return;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`http://localhost:5000/hubs/chat`, {
        skipNegotiation: true,
        transport: signalR.HttpTransportType.WebSockets
      })
      .withAutomaticReconnect()
      .build();

    connection.on("ReceiveChatResponse", (res) => {
      setMessages(prev => [...prev, {
        id: Date.now(),
        sender: 'assistant',
        text: res.replyMessage,
        isOnTopic: res.isOnTopic,
        isComplete: res.isComplete,
        extractedParameters: res.extractedParameters,
        timestamp: new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
      }]);
      setIsChatLoading(false);

      if (res.extractedParameters) {
        if (res.extractedParameters.assetId) {
          setSelectedAssetId(res.extractedParameters.assetId.toString());
        }
        if (res.extractedParameters.timeRange) {
          setTimeRange(res.extractedParameters.timeRange);
        }
        if (res.extractedParameters.mode) {
          setIncludeFullRawData(res.extractedParameters.mode === 'raw');
        }
      }
    });

    connection.on("ReceiveHistory", (historyItems) => {
      if (historyItems && historyItems.length > 0) {
        const formatted = historyItems.map(h => ({
          id: h.id || Math.random(),
          sender: h.role,
          text: h.content,
          isOnTopic: true,
          timestamp: new Date(h.createdAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
        }));
        setMessages(formatted);
      }
    });

    connection.start()
      .then(() => {
        setWsConnected(true);
        connection.invoke("GetHistory", sessionId);
      })
      .catch(err => {
        console.warn("SignalR Connection failed, falling back to REST API", err);
        setWsConnected(false);
      });

    hubConnectionRef.current = connection;

    return () => {
      connection.stop();
    };
  }, [sessionId]);

  // Fetch signals when asset changes
  useEffect(() => {
    if (selectedAssetId) {
      fetchSignals(selectedAssetId);
    } else {
      setSignals([]);
      setSelectedSignalIds([]);
    }
  }, [selectedAssetId]);

  useEffect(() => {
    chatEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages, isChatLoading]);

  const fetchAssets = async () => {
    try {
      const res = await fetch(`${API_BASE_URL}/reports/assets`);
      if (res.ok) {
        const data = await res.json();
        setAssets(data);
        if (data.length > 0) {
          setSelectedAssetId(data[0].assetId);
        }
      }
    } catch (err) {
      console.error('Failed to load assets', err);
      setReportError('Unable to connect to .NET Backend. Ensure API is running on http://localhost:5000');
    }
  };

  const fetchSignals = async (assetId) => {
    try {
      const res = await fetch(`${API_BASE_URL}/reports/assets/${assetId}/signals`);
      if (res.ok) {
        const data = await res.json();
        setSignals(data);
        // Default select first 6 critical signals
        const defaultSelected = data.slice(0, 6).map(s => s.signalId);
        setSelectedSignalIds(defaultSelected);
      }
    } catch (err) {
      console.error('Failed to load signals', err);
    }
  };

  const toggleSignal = (signalId) => {
    setSelectedSignalIds(prev => 
      prev.includes(signalId) ? prev.filter(id => id !== signalId) : [...prev, signalId]
    );
  };

  const selectAllSignals = () => {
    setSelectedSignalIds(signals.map(s => s.signalId));
  };

  const clearSignals = () => {
    setSelectedSignalIds([]);
  };

  /**
   * Compute from/to dates from a timeRange string (e.g. "5.5d", "24h", "2w").
   * Always called with the actual value to use — never relies on potentially-stale React state.
   */
  const computeDateRange = (rangeStr) => {
    const to = new Date();
    let from = new Date(to);

    const str = (rangeStr || '').toString().toLowerCase().trim();
    if (!str) {
      from.setDate(to.getDate() - 7);
      return { from: from.toISOString(), to: to.toISOString() };
    }

    const num = parseFloat(str.replace(/[^\d.]/g, ''));

    if (!isNaN(num) && num > 0) {
      if (str.includes('h') && !str.includes('d') && !str.includes('day')) {
        // Hours: "24h", "12h", "1.5h"
        from.setTime(to.getTime() - Math.round(num * 3600 * 1000));
      } else if (str.includes('w') || str.includes('week')) {
        // Weeks: "1w", "2.5w", "2 weeks"
        from.setTime(to.getTime() - Math.round(num * 7 * 86400 * 1000));
      } else if (str === `${num}m` || str.includes('min')) {
        // Minutes: "30m", "45min" — rare but guard against LLM emitting it
        from.setTime(to.getTime() - Math.round(num * 60 * 1000));
      } else {
        // Days: "5d", "5.5d", "5 days", "20", bare numbers default to days
        from.setTime(to.getTime() - Math.round(num * 86400 * 1000));
      }
    } else {
      from.setDate(to.getDate() - 7);
    }

    return { from: from.toISOString(), to: to.toISOString() };
  };

  // Convenience wrapper that reads from React state (for the sidebar controls)
  const getTimeRangeDates = () => computeDateRange(timeRange);

  const handleGeneratePreview = async () => {
    if (!selectedAssetId) return;

    setIsGenerating(true);
    setReportError(null);

    const { from, to } = getTimeRangeDates();

    try {
      const res = await fetch(`${API_BASE_URL}/reports/preview`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          assetId: parseInt(selectedAssetId),
          signalIds: selectedSignalIds.length > 0 ? selectedSignalIds : null,
          from,
          to,
          includeEvents,
          includeAlerts,
          includeInsights,
          includeCharts
        })
      });

      if (!res.ok) throw new Error(`Server returned HTTP ${res.status}`);
      const data = await res.json();
      setReportData(data);
    } catch (err) {
      console.error(err);
      setReportError(`Failed to generate report: ${err.message}`);
    } finally {
      setIsGenerating(false);
    }
  };

  // Background PDF Queue Handlers
  const fetchJobs = async () => {
    try {
      const res = await fetch(`${API_BASE_URL}/reports/jobs`);
      if (res.ok) {
        const data = await res.json();
        setJobs(data);
      }
    } catch (err) {
      console.error('Failed to load queue jobs', err);
    }
  };

  useEffect(() => {
    fetchJobs();
  }, []);

  useEffect(() => {
    const hasActive = jobs.some(j => {
      const s = (j.statusText || j.status || '').toString().toLowerCase();
      return s === 'queued' || s === '0' || s === 'processing' || s === '1';
    });

    if (!hasActive) return;

    const timer = setInterval(() => {
      fetchJobs();
    }, 2000);

    return () => clearInterval(timer);
  }, [jobs]);

  const handleQueuePdf = async () => {
    if (!selectedAssetId) return;
    // Read state at call time — safe because this is triggered from the sidebar controls
    // where selectedAssetId and timeRange are already up-to-date.
    await _queuePdfJob(parseInt(selectedAssetId), timeRange);
  };

  /**
   * Core queue function. Accepts explicit assetId + rangeStr or absolute fromDate/toDate.
   * Called from chat bubble with exact LLM-extracted values — no stale React state dependency.
   */
  const _queuePdfJob = async (assetId, rangeStr, fromDateIso = null, toDateIso = null) => {
    if (!assetId) return;

    setIsQueueing(true);
    setReportError(null);

    // Prefer absolute dates from LLM; fall back to relative range computation
    let from, to;
    if (fromDateIso) {
      from = fromDateIso;
      to = toDateIso || new Date().toISOString();
    } else {
      ({ from, to } = computeDateRange(rangeStr));
    }

    try {
      const res = await fetch(`${API_BASE_URL}/reports/queue`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          assetId,
          signalIds: selectedSignalIds.length > 0 ? selectedSignalIds : null,
          from,
          to,
          includeEvents,
          includeAlerts,
          includeInsights,
          includeCharts,
          includeFullRawData
        })
      });

      if (!res.ok) throw new Error(`Server returned HTTP ${res.status}`);
      await fetchJobs();
    } catch (err) {
      console.error(err);
      setReportError(`Failed to queue PDF: ${err.message}`);
    } finally {
      setIsQueueing(false);
    }
  };

  const handleDownloadJob = (jobId, assetName) => {
    const url = `${API_BASE_URL}/reports/jobs/${jobId}/download`;
    const a = document.createElement('a');
    a.href = url;
    a.download = `Full_Report_${assetName || 'Asset'}_${new Date().toISOString().slice(0, 10)}.pdf`;
    document.body.appendChild(a);
    a.click();
    a.remove();
  };

  const handleCancelJob = async (jobId) => {
    try {
      const res = await fetch(`${API_BASE_URL}/reports/jobs/${jobId}`, {
        method: 'DELETE'
      });
      if (res.ok) {
        await fetchJobs();
      } else {
        const errData = await res.json();
        setReportError(errData.error || 'Failed to cancel job');
      }
    } catch (err) {
      console.error('Failed to cancel job', err);
      setReportError(`Failed to cancel job: ${err.message}`);
    }
  };

  // Chat Handlers
  const handleSendChat = async (text) => {
    const messageText = text || chatInput;
    if (!messageText.trim() || isChatLoading) return;

    const userMessage = {
      id: Date.now(),
      sender: 'user',
      text: messageText.trim(),
      timestamp: new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
    };

    setMessages(prev => [...prev, userMessage]);
    setChatInput('');
    setIsChatLoading(true);
    setChatError(null);

    // 1. Primary: Send via SignalR WebSockets
    if (wsConnected && hubConnectionRef.current) {
      try {
        await hubConnectionRef.current.invoke("SendMessage", sessionId, messageText.trim());
        return;
      } catch (err) {
        console.warn("SignalR SendMessage failed, falling back to HTTP REST", err);
      }
    }

    // 2. Fallback: Send via HTTP REST
    try {
      const res = await fetch(`${API_BASE_URL}/chat`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          message: messageText.trim(),
          sessionId
        })
      });

      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const data = await res.json();

      setMessages(prev => [...prev, {
        id: Date.now() + 1,
        sender: 'assistant',
        text: data.reply,
        isOnTopic: data.isOnTopic,
        isComplete: data.isComplete,
        extractedParameters: data.extractedParameters,
        timestamp: new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
      }]);

      if (data.extractedParameters) {
        if (data.extractedParameters.assetId) {
          setSelectedAssetId(data.extractedParameters.assetId.toString());
        }
        if (data.extractedParameters.timeRange) {
          setTimeRange(data.extractedParameters.timeRange);
        }
      }
    } catch (err) {
      console.error(err);
      setChatError(`Failed to send message: ${err.message}`);
    } finally {
      setIsChatLoading(false);
    }
  };

  // Format chart time-series data for Recharts
  const formatChartData = () => {
    if (!reportData || !reportData.signals || reportData.signals.length === 0) return [];

    const signalsWithData = reportData.signals.filter(s => s.dataPoints && s.dataPoints.length > 0);
    if (signalsWithData.length === 0) return [];

    // Use timestamps of first signal as base
    const basePoints = signalsWithData[0].dataPoints;
    return basePoints.map((pt, idx) => {
      const row = {
        time: new Date(pt.time).toLocaleDateString([], { month: 'numeric', day: 'numeric', hour: '2-digit', minute: '2-digit' })
      };
      signalsWithData.slice(0, 5).forEach(sig => {
        if (sig.dataPoints[idx]) {
          row[sig.name] = sig.dataPoints[idx].value;
        }
      });
      return row;
    });
  };

  const chartData = formatChartData();
  const activeSignalsForChart = reportData?.signals?.filter(s => s.dataPoints?.length > 0).slice(0, 5) || [];

  return (
    <div className="layout-root">
      {/* ALWAYS OPEN LEFT SIDEBAR */}
      <aside className="app-sidebar">
        <div className="sidebar-header">
          <div className="sidebar-logo">
            <Activity size={22} />
          </div>
          <div className="sidebar-brand-info">
            <h2 className="sidebar-title">Telemetry Hub</h2>
            <span className="sidebar-subtitle">RabbitMQ Queue Manager</span>
          </div>
        </div>

        {/* PDF Generation Queue Container */}
        <div className="sidebar-section queue-section">
          <div className="sidebar-section-title queue-header">
            <div className="title-left">
              <ListOrdered size={15} className="icon-indigo" />
              <span>PDF Job Queue</span>
              {jobs.filter(j => {
                const s = (j.statusText || j.status || '').toString().toLowerCase();
                return s === 'queued' || s === '0' || s === 'processing' || s === '1';
              }).length > 0 && (
                <span className="queue-active-count">
                  <RefreshCw size={9} className="spin" />
                  {jobs.filter(j => {
                    const s = (j.statusText || j.status || '').toString().toLowerCase();
                    return s === 'queued' || s === '0' || s === 'processing' || s === '1';
                  }).length} Active
                </span>
              )}
            </div>
            <button 
              type="button" 
              className="btn-refresh-sm"
              onClick={fetchJobs}
              title="Refresh background job status"
            >
              <RefreshCw size={12} />
            </button>
          </div>

          <div className="sidebar-queue-body">
            {jobs.length === 0 ? (
              <div className="sidebar-queue-empty">
                <Clock size={24} className="empty-icon-muted" />
                <p>Queue is empty</p>
                <span>Click "Queue Full PDF" to generate unaggregated report asynchronously</span>
              </div>
            ) : (
              <div className="sidebar-queue-list">
                {jobs.map(job => {
                  const statusStr = (job.statusText || (job.status === 2 ? 'Completed' : job.status === 1 ? 'Processing' : job.status === 3 ? 'Failed' : job.status === 4 ? 'Cancelled' : 'Queued'));
                  const isCompleted = statusStr.toLowerCase() === 'completed';
                  const isProcessing = statusStr.toLowerCase() === 'processing';
                  const isFailed = statusStr.toLowerCase() === 'failed';
                  const isCancelled = statusStr.toLowerCase() === 'cancelled';
                  const isQueued = statusStr.toLowerCase() === 'queued';

                  return (
                    <div 
                      key={job.jobId} 
                      className={`sidebar-queue-item ${isCompleted ? 'completed' : isProcessing ? 'processing' : isFailed ? 'failed' : isCancelled ? 'cancelled' : ''}`}
                    >
                      <div className="queue-item-header">
                        <span className="queue-item-name">{job.assetName}</span>
                        <span className={`badge-sm ${
                          isCompleted ? 'badge-success' : 
                          isProcessing ? 'badge-processing' : 
                          isFailed ? 'badge-danger' : 
                          isCancelled ? 'badge-muted' : 'badge-warning'
                        }`}>
                          {isProcessing && <RefreshCw size={9} className="spin" />}
                          {isCompleted && <CheckCircle2 size={9} />}
                          {statusStr.toUpperCase()}
                        </span>
                      </div>

                      <div className="queue-item-msg">
                        {isFailed ? (job.errorMessage || job.statusMessage || 'Failed to generate report') : (job.statusMessage || (isQueued ? 'Queued in RabbitMQ...' : ''))}
                      </div>

                      {(isProcessing || isQueued) && (
                        <div className="queue-progress-bar">
                          <div 
                            className="queue-progress-fill" 
                            style={{ width: `${Math.max(8, job.progressPercentage || 10)}%` }}
                          />
                        </div>
                      )}

                      <div className="queue-item-footer">
                        <span className="queue-time">
                          {new Date(job.createdAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                          {job.fileSizeBytes ? ` • ${(job.fileSizeBytes / 1024).toFixed(0)}KB` : ''}
                        </span>

                        {isCompleted ? (
                          <button 
                            className="btn-download-icon"
                            onClick={() => handleDownloadJob(job.jobId, job.assetName)}
                            title="Download Completed PDF"
                          >
                            <Download size={12} /> Download PDF
                          </button>
                        ) : (isQueued || isProcessing) ? (
                          <button
                            type="button"
                            className="btn-cancel-icon"
                            onClick={() => handleCancelJob(job.jobId)}
                            title="Cancel Job"
                          >
                            <Trash2 size={12} /> Cancel
                          </button>
                        ) : null}
                      </div>
                    </div>
                  );
                })}
              </div>
            )}
          </div>
        </div>
      </aside>

      {/* MAIN WORKSPACE */}
      <div className="main-workspace">
        {/* Top Navbar */}
        <header className="top-navbar">
          <div className="nav-brand">
            <div>
              <h1 className="nav-title">Industrial Asset Telemetry & Publication Reporting</h1>
              <p className="nav-subtitle">TimescaleDB Telemetry • Dockerized RabbitMQ Worker • Qwen 2.5 Coder 7B Guard</p>
            </div>
          </div>

          <div className="nav-badges">
            <span className={`system-pill ${wsConnected ? 'active' : ''}`}>
              <span className={`dot ${wsConnected ? '' : 'danger'}`}></span> WebSockets ({wsConnected ? 'Connected' : 'Connecting...'})
            </span>
            <span className="system-pill active">
              <span className="dot"></span> RabbitMQ Broker (5672)
            </span>
            <span className="system-pill">
              <span className="dot db"></span> PostgreSQL (5432)
            </span>
            <span className="system-pill">
              <span className="dot api"></span> .NET API (5000)
            </span>
          </div>
        </header>

        {/* Main Split Screen Container */}
        <div className="split-view">
          {/* ================= LEFT COLUMN: REPORT GENERATION UI ================= */}
          <section className="left-panel">
            {/* Controls Bar */}
            <div className="panel-card config-card">
              <div className="card-header">
                <div className="card-title">
                  <Sliders size={18} />
                  <span>Report Parameters</span>
                </div>
                <div className="action-buttons" style={{ width: '100%' }}>
                  <button 
                    className="btn btn-primary" 
                    onClick={handleQueuePdf}
                    disabled={isQueueing || !selectedAssetId}
                    title={includeFullRawData ? "Queue report with all unaggregated data rows" : "Queue report with graphs & trend curves only"}
                    style={{ width: '100%', justifyContent: 'center', padding: '0.75rem 1rem', fontSize: '0.925rem' }}
                  >
                    <ListOrdered size={16} />
                    {isQueueing ? "Enqueuing PDF Job..." : (includeFullRawData ? "Queue Full PDF Report (All Rows)" : "Queue Graphs & Trends PDF Report")}
                  </button>
                </div>
              </div>

              {/* REPORT CONTENT MODE TOGGLE SWITCH */}
              <div className="report-mode-toggle-card">
                <div className="mode-info">
                  <div className="mode-title-row">
                    {includeFullRawData ? <Database size={16} className="mode-icon text-indigo" /> : <TrendingUp size={16} className="mode-icon text-amber" />}
                    <span className="mode-title">Report Content: <strong>{includeFullRawData ? "Full Data Table (All Rows)" : "Graphs & Trends Only"}</strong></span>
                  </div>
                  <div className="mode-subtitle">
                    {includeFullRawData 
                      ? "PDF will include summary metrics plus complete chronological raw data rows on subsequent pages." 
                      : "PDF will include summary metrics & enlarged trend curves, skipping raw data table rows."}
                  </div>
                </div>
                <label className="switch-control">
                  <input 
                    type="checkbox" 
                    checked={includeFullRawData} 
                    onChange={(e) => setIncludeFullRawData(e.target.checked)} 
                  />
                  <span className="slider-round"></span>
                </label>
              </div>

              <div className="form-grid">
                {/* Asset Dropdown */}
                <div className="form-group">
                  <label className="form-label">
                    <Layers size={14} /> Target Asset
                  </label>
                  <select 
                    className="form-select"
                    value={selectedAssetId}
                    onChange={(e) => setSelectedAssetId(e.target.value)}
                  >
                    {assets.map(a => (
                      <option key={a.assetId} value={a.assetId}>
                        {a.name} ({a.location || a.assetType})
                      </option>
                    ))}
                  </select>
                </div>

                {/* Time Range */}
                <div className="form-group">
                  <label className="form-label">
                    <Calendar size={14} /> Telemetry Timeframe
                  </label>
                  <div className="pill-group">
                    {['24h', '7d', '14d', '30d'].map(range => (
                      <button
                        key={range}
                        type="button"
                        className={`pill-btn ${timeRange === range ? 'active' : ''}`}
                        onClick={() => setTimeRange(range)}
                      >
                        {range.toUpperCase()}
                      </button>
                    ))}
                  </div>
                </div>
              </div>

              {/* Checkbox Options */}
              <div className="toggles-row">
                <label className="checkbox-label">
                  <input 
                    type="checkbox" 
                    checked={includeEvents} 
                    onChange={(e) => setIncludeEvents(e.target.checked)} 
                  />
                  <span>Include Excursion Events</span>
                </label>

                <label className="checkbox-label">
                  <input 
                    type="checkbox" 
                    checked={includeAlerts} 
                    onChange={(e) => setIncludeAlerts(e.target.checked)} 
                  />
                  <span>Include System Alerts</span>
                </label>

                <label className="checkbox-label">
                  <input 
                    type="checkbox" 
                    checked={includeCharts} 
                    onChange={(e) => setIncludeCharts(e.target.checked)} 
                  />
                  <span>Render Trend Charts</span>
                </label>

                <label className="checkbox-label">
                  <input 
                    type="checkbox" 
                    checked={includeInsights} 
                    onChange={(e) => setIncludeInsights(e.target.checked)} 
                  />
                  <span>AI / Engineering Insights</span>
                </label>
              </div>

              {/* Signals Selection Pills */}
              <div className="signals-section">
                <div className="signals-header">
                  <span className="form-label">
                    Signals ({selectedSignalIds.length} of {signals.length} selected)
                  </span>
                  <div className="quick-actions">
                    <button type="button" onClick={selectAllSignals}>Select All</button>
                    <span>•</span>
                    <button type="button" onClick={clearSignals}>Clear</button>
                  </div>
                </div>

                <div className="signals-scroll-box">
                  {signals.map(s => {
                    const isChecked = selectedSignalIds.includes(s.signalId);
                    return (
                      <div 
                        key={s.signalId} 
                        className={`signal-chip ${isChecked ? 'selected' : ''}`}
                        onClick={() => toggleSignal(s.signalId)}
                      >
                        {isChecked ? <CheckSquare size={14} /> : <Square size={14} />}
                        <span className="sig-name">{s.name}</span>
                        {s.unit && <span className="sig-unit">{s.unit}</span>}
                      </div>
                    );
                  })}
                </div>
              </div>

              {reportError && (
                <div className="error-banner">
                  <AlertCircle size={16} />
                  <span>{reportError}</span>
                </div>
              )}
            </div>
          </section>

        {/* ================= RIGHT COLUMN: CHATBOT UI ================= */}
        <aside className="right-panel">
          <div className="chat-wrapper">
            <div className="chat-header">
              <div className="chat-header-title">
                <Bot size={20} className="icon-indigo" />
                <div>
                  <h3>Sensor Report Assistant</h3>
                  <p>Guard Step • Qwen 2.5 Coder 7B</p>
                </div>
              </div>

              <button 
                className="icon-btn-sm" 
                onClick={() => setMessages([])} 
                title="Clear Chat"
              >
                <Trash2 size={16} />
              </button>
            </div>

            {/* Chat Messages */}
            <div className="chat-messages">
              {messages.length === 0 ? (
                <div className="chat-empty">
                  <div className="chat-bot-icon">
                    <Bot size={28} />
                  </div>
                  <h4>How can I help with your reports?</h4>
                  <p>Ask about machine telemetry, temperatures, pump vibrations, or generate insights.</p>

                  <div className="chat-suggestions">
                    <button 
                      onClick={() => handleSendChat("Give me report for pump 3 for last 2 days")}
                      className="chat-chip on-topic"
                    >
                      <ChevronRight size={13} /> "Give me report for pump 3 for last 2 days"
                    </button>
                    <button 
                      onClick={() => handleSendChat("Check high vibration alarm logs for motor 2")}
                      className="chat-chip on-topic"
                    >
                      <ChevronRight size={13} /> "Check vibration alarms for motor 2"
                    </button>
                    <button 
                      onClick={() => handleSendChat("Who is Shah Rukh Khan?")}
                      className="chat-chip off-topic"
                    >
                      <ChevronRight size={13} /> "Who is Shah Rukh Khan?" (Guard Refusal)
                    </button>
                  </div>
                </div>
              ) : (
                messages.map(m => (
                  <div key={m.id} className={`chat-bubble-row ${m.sender}`}>
                    <div className="chat-avatar">
                      {m.sender === 'user' ? <User size={16} /> : <Bot size={16} />}
                    </div>
                    <div className="chat-bubble-content">
                      {m.sender === 'assistant' && (
                        <div className="guard-tag-wrapper">
                          <span className={`guard-tag ${m.isOnTopic ? 'on-topic' : 'off-topic'}`}>
                            {m.isOnTopic ? (
                              <><CheckCircle2 size={11} /> ON-TOPIC GUARD</>
                            ) : (
                              <><AlertCircle size={11} /> OFF-TOPIC REFUSAL</>
                            )}
                          </span>
                        </div>
                      )}
                      <div className="bubble-text">{m.text}</div>

                      {/* Extracted Intent Parameters Card */}
                      {m.sender === 'assistant' && m.extractedParameters && (m.extractedParameters.assetName || m.extractedParameters.timeRange || m.extractedParameters.fromDate) && (
                        <div className="extracted-params-card">
                          <div className="params-header">
                            <Zap size={13} className="text-amber" />
                            <span>Extracted Parameters</span>
                            {m.isComplete && <span className="complete-tag"><CheckCircle2 size={10} /> COMPLETE</span>}
                          </div>
                          <div className="params-body">
                            {m.extractedParameters.assetName && (
                              <div className="param-chip">Asset: <strong>{m.extractedParameters.assetName}</strong></div>
                            )}
                            {m.extractedParameters.timeRange && (
                              <div className="param-chip">Time: <strong>{m.extractedParameters.timeRange}</strong></div>
                            )}
                            {m.extractedParameters.fromDate && (
                              <div className="param-chip">
                                From: <strong>{new Date(m.extractedParameters.fromDate).toLocaleString()}</strong>
                              </div>
                            )}
                            {m.extractedParameters.toDate && (
                              <div className="param-chip">
                                To: <strong>{new Date(m.extractedParameters.toDate).toLocaleString()}</strong>
                              </div>
                            )}
                            {m.extractedParameters.mode && (
                              <div className="param-chip">Format: <strong>{m.extractedParameters.mode === 'raw' ? 'Full Rows' : 'Graphs Only'}</strong></div>
                            )}
                          </div>
                          {m.isComplete && (
                            <button 
                              type="button"
                              className="btn-queue-intent" 
                              onClick={() => {
                                const assetId = m.extractedParameters.assetId;
                                const rangeStr = m.extractedParameters.timeRange;
                                const fromDate = m.extractedParameters.fromDate;
                                const toDate = m.extractedParameters.toDate;
                                if (assetId) setSelectedAssetId(assetId.toString());
                                if (rangeStr) setTimeRange(rangeStr);
                                // Pass exact values from LLM — no stale state
                                _queuePdfJob(
                                  assetId || parseInt(selectedAssetId),
                                  rangeStr || timeRange,
                                  fromDate || null,
                                  toDate || null
                                );
                              }}
                            >
                              <ListOrdered size={12} /> Queue PDF Report Now
                            </button>
                          )}
                        </div>
                      )}

                      <div className="bubble-time">{m.timestamp}</div>
                    </div>
                  </div>
                ))
              )}

              {isChatLoading && (
                <div className="chat-bubble-row assistant">
                  <div className="chat-avatar">
                    <Bot size={16} />
                  </div>
                  <div className="chat-bubble-content">
                    <div className="bubble-text typing">
                      <span></span><span></span><span></span>
                    </div>
                  </div>
                </div>
              )}

              <div ref={chatEndRef} />
            </div>

            {/* Chat Input */}
            <form 
              className="chat-input-bar"
              onSubmit={(e) => {
                e.preventDefault();
                handleSendChat();
              }}
            >
              <input 
                type="text" 
                value={chatInput}
                onChange={(e) => setChatInput(e.target.value)}
                placeholder="Ask about sensors, reports, alarms..."
                disabled={isChatLoading}
              />
              <button 
                type="submit" 
                className="chat-send-btn" 
                disabled={!chatInput.trim() || isChatLoading}
              >
                <Send size={16} />
              </button>
            </form>
          </div>
        </aside>
      </div>
    </div>
  </div>
);
}

export default App;
