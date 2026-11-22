//+------------------------------------------------------------------+
//| JJ_Simon_FairPrice_Research_v1.mq5                               |
//| Standalone research interpretation of JJ Simon's PUBLIC rules.  |
//| NOT his official EA and NOT an exact reproduction of discretion.|
//| Built for real NQ 1-minute OHLC via an MT5 custom futures symbol.|
//| Historical simulation ONLY.                                     |
//+------------------------------------------------------------------+
#property copyright "Independent research implementation"
#property version   "1.00"
#property strict

#include <Trade/Trade.mqh>

enum ENUM_JJ_CONFIRM
  {
   JJ_DISPLACEMENT_OR_BOS=0, // TradeZella description / interview interpretation
   JJ_DISPLACEMENT_AND_BOS=1,// Strict conjunction variant
   JJ_DISPLACEMENT_ONLY=2,
   JJ_BOS_ONLY=3
  };

enum ENUM_JJ_STOPS
  {
   JJ_FIXED_25_38=0,       // Published evaluation-style example
   JJ_ATR_TIERS_1P5R=1    // JJ's published FX Replay variant
  };

enum ENUM_JJ_DATA_TIME
  {
   JJ_DATA_UTC=0,          // Recommended for imported historical CSV
   JJ_DATA_EASTERN=1,      // Bars already labelled US Eastern local time
   JJ_DATA_FIXED_UTC_OFFSET=2
  };

input group "01 | Research scope"
input bool              InpTesterOnly             = true;   // Refuse live execution by default
input bool              InpTradeNYMorning         = true;   // 09:30-11:00 ET
input bool              InpTradeNYAfternoon       = false;  // 14:00-15:00 ET
input bool              InpTradeContinuation      = true;
input bool              InpTradeReversion         = true;
input ENUM_JJ_CONFIRM   InpConfirmation           = JJ_DISPLACEMENT_OR_BOS;
input bool              InpAllowOpeningBarSignal  = false;  // Avoid opening-bar-only entry

input group "02 | Ambiguities - explicitly disclosed proxies"
input int               InpSwingWidth             = 1;      // Fractal bars on BOTH sides
input int               InpBOSLookback            = 30;     // Completed M1 bars
input int               InpContinuationMinutes    = 5;      // From session start
input bool              InpUseCounterwickFilter   = false;  // OPTIONAL FX Replay interpretation
input double            InpMaxCounterwickFraction = 0.20;   // Fraction of entire candle range
input bool              InpRequireFairBeyondTP    = false;  // Optional, not JJ evaluation rule

input group "03 | Stop, target and contracts"
input ENUM_JJ_STOPS     InpStopModel             = JJ_FIXED_25_38;
input double            InpVolumeContracts       = 1.0;    // For custom futures symbol: 1 = 1 NQ
input bool              InpWidenForWideOpening   = false;  // 50/76 and half size if opening >25
input int               InpATRPeriod             = 14;
input int               InpMaxRevConsecLosses    = 3;
input ulong             InpMagic                 = 9126021;
input int               InpDeviationPoints       = 20;     // MT5 symbol points, NOT NQ index points

input group "04 | Import time and audits"
input ENUM_JJ_DATA_TIME InpDataTimeZone           = JJ_DATA_UTC;
input int               InpFixedUTCOffsetHours    = 0;      // If fixed-offset mode selected
input bool              InpExportSignalAuditCSV   = true;

CTrade g_trade;
datetime g_lastBar=0;
int g_atrHandle=INVALID_HANDLE;
int g_audit=INVALID_HANDLE;

int g_sessionKey=-1;
int g_continuationCount=0;
int g_reversionConsecLosses=0;
int g_liveSetup=0;             // 1=continuation, 2=reversion
int g_liveSessionKey=-1;
double g_openEntryCosts=0.0;

// Convert imported bar timestamps to true US/Eastern including DST (post-2007 rules).
// This intentionally uses bar timestamps, NEVER local PC clock or TimeCurrent.
datetime ComposeUTC(const int yy,const int mm,const int dd,const int hh)
  {
   MqlDateTime v;
   ZeroMemory(v);
   v.year=yy; v.mon=mm; v.day=dd; v.hour=hh;
   return StructToTime(v);
  }

int FirstSunday(const int yy,const int mm)
  {
   MqlDateTime v;
   TimeToStruct(ComposeUTC(yy,mm,1,0),v);
   return 1 + (7-v.day_of_week)%7;
  }

bool IsEasternDST(const datetime utc)
  {
   MqlDateTime x;
   TimeToStruct(utc,x);
   datetime start=ComposeUTC(x.year,3,FirstSunday(x.year,3)+7,7); // 2am EST=07 UTC
   datetime finish=ComposeUTC(x.year,11,FirstSunday(x.year,11),6); // 2am EDT=06 UTC
   return (utc>=start && utc<finish);
  }

datetime ToEastern(const datetime raw)
  {
   if(InpDataTimeZone==JJ_DATA_EASTERN)
      return raw;
   if(InpDataTimeZone==JJ_DATA_FIXED_UTC_OFFSET)
      return raw + InpFixedUTCOffsetHours*3600;
   return raw + (IsEasternDST(raw)?-4:-5)*3600;
  }

int DayCode(const MqlDateTime &x)
  {
   return x.year*10000+x.mon*100+x.day;
  }

int MinuteOfDay(const MqlDateTime &x)
  {
   return x.hour*60+x.min;
  }

bool SessionForBar(const datetime raw,int &startMinute,int &endMinute,int &slot,int &key)
  {
   MqlDateTime x;
   TimeToStruct(ToEastern(raw),x);
   const int m=MinuteOfDay(x);
   if(InpTradeNYMorning && m>=570 && m<660)
     {startMinute=570; endMinute=660; slot=1; key=DayCode(x)*10+1; return true;}
   if(InpTradeNYAfternoon && m>=840 && m<900)
     {startMinute=840; endMinute=900; slot=2; key=DayCode(x)*10+2; return true;}
   return false;
  }

// Locate the opening 1-minute bar on the same Eastern session/date.
// Never substitute another bar if the exact session opening bar is absent.
bool FindOpeningBar(const MqlRates &bars[],const int n,const int targetKey,
                    const int startMinute,int &openingIdx)
  {
   openingIdx=-1;
   for(int i=0;i<n;i++)
     {
      MqlDateTime x;
      TimeToStruct(ToEastern(bars[i].time),x);
      if(MinuteOfDay(x)!=startMinute) continue;
      int sm=0,em=0,s=0,key=0;
      if(SessionForBar(bars[i].time,sm,em,s,key) && key==targetKey)
        {openingIdx=i; return true;}
     }
   return false;
  }

bool IsUp(const MqlRates &x) {return x.close>x.open;}
bool IsDown(const MqlRates &x) {return x.close<x.open;}

bool Displacement(const MqlRates &bars[],const int n,const int dir)
  {
   if(n<3) return false;
   const MqlRates c=bars[1]; // Completed signal candle
   const MqlRates p=bars[2]; // Completed preceding candle
   if(bars[1].time-bars[2].time!=60) return false;
   double body=MathAbs(c.close-c.open);
   double oldBody=MathAbs(p.close-p.open);
   bool valid=false;
   if(dir>0)
      valid=IsUp(c) && IsDown(p) && body>oldBody && c.close>p.high;
   else
      valid=IsDown(c) && IsUp(p) && body>oldBody && c.close<p.low;
   if(!valid) return false;
   if(!InpUseCounterwickFilter) return true;
   double range=c.high-c.low;
   if(range<=0) return false;
   double counter=(dir>0) ? MathMax(0.0,c.open-c.low) : MathMax(0.0,c.high-c.open);
   return (counter/range)<=InpMaxCounterwickFraction;
  }

bool PivotHigh(const MqlRates &bars[],const int n,const int idx)
  {
   int w=InpSwingWidth;
   if(idx-w<2 || idx+w>=n) return false; // Swing must precede signal
   for(int k=1;k<=w;k++)
      if(bars[idx].high<=bars[idx-k].high || bars[idx].high<=bars[idx+k].high)
         return false;
   return true;
  }

bool PivotLow(const MqlRates &bars[],const int n,const int idx)
  {
   int w=InpSwingWidth;
   if(idx-w<2 || idx+w>=n) return false;
   for(int k=1;k<=w;k++)
      if(bars[idx].low>=bars[idx-k].low || bars[idx].low>=bars[idx+k].low)
         return false;
   return true;
  }

bool BreakOfStructure(const MqlRates &bars[],const int n,const int dir)
  {
   if(n<6) return false;
   int start=InpSwingWidth+2;
   int limit=(int)MathMin((double)(n-InpSwingWidth-1),(double)(InpBOSLookback+2));
   // Most recent *confirmed* swing is used: no future bars, no wick-only breaks.
   for(int i=start;i<limit;i++)
     {
      if(dir>0 && PivotHigh(bars,n,i))
        {
         double level=bars[i].high;
         return bars[2].close<=level && bars[1].close>level;
        }
      if(dir<0 && PivotLow(bars,n,i))
        {
         double level=bars[i].low;
         return bars[2].close>=level && bars[1].close<level;
        }
     }
   return false;
  }

bool ConfirmedEntry(const MqlRates &bars[],const int n,const int dir,string &tag)
  {
   bool d=Displacement(bars,n,dir);
   bool b=BreakOfStructure(bars,n,dir);
   tag=(d && b)?"DB":(d?"D":(b?"B":"-"));
   if(InpConfirmation==JJ_DISPLACEMENT_AND_BOS) return d && b;
   if(InpConfirmation==JJ_DISPLACEMENT_ONLY)    return d;
   if(InpConfirmation==JJ_BOS_ONLY)             return b;
   return d || b;
  }

bool HasPositionOnSymbol()
  {
   for(int i=PositionsTotal()-1;i>=0;i--)
     {
      ulong ticket=PositionGetTicket(i);
      if(ticket>0 && PositionSelectByTicket(ticket) && PositionGetString(POSITION_SYMBOL)==_Symbol)
         return true;
     }
   return false;
  }

bool DetermineBracket(const MqlRates &opening,const double volumeBase,double &stopPts,
                      double &targetPts,double &volume)
  {
   stopPts=25.0; targetPts=38.0; volume=volumeBase;
   if(InpStopModel==JJ_ATR_TIERS_1P5R)
     {
      double atr[1];
      if(g_atrHandle==INVALID_HANDLE || CopyBuffer(g_atrHandle,0,1,1,atr)!=1)
         return false;
      if(atr[0]<7.0) stopPts=16.5;
      else if(atr[0]>20.0) stopPts=50.0;
      targetPts=1.5*stopPts;
     }
   else if(InpWidenForWideOpening && opening.high-opening.low>25.0)
     {
      stopPts=50.0; targetPts=76.0; volume=volumeBase*0.5;
     }
   return true;
  }

// Futures prices must snap to the EXCHANGE tick size (normally 0.25 NQ points).
double RoundTick(const double x)
  {
   double step=SymbolInfoDouble(_Symbol,SYMBOL_TRADE_TICK_SIZE);
   if(step<=0.0) return NormalizeDouble(x,_Digits);
   return NormalizeDouble(MathRound(x/step)*step,_Digits);
  }

bool VolumeAllowed(const double vol)
  {
   double minimum=SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_MIN);
   double maximum=SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_MAX);
   double step=SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_STEP);
   if(vol<minimum-1e-8 || vol>maximum+1e-8 || step<=0) return false;
   double units=vol/step;
   return MathAbs(units-MathRound(units))<1e-6;
  }

void EmitSignal(const int mode,const int dir,const int key,const double fair,
                const double stopPts,const double tpPts,const double vol,
                const double fillReference,const string tag,const datetime signalTime)
  {
   string side=(dir>0)?"BUY":"SELL";
   string setup=(mode==1)?"CONTINUATION":"REVERSION";
   PrintFormat("JJ SIGNAL | %s | %s | %s | ET=%s | stop=%.2f TP=%.2f vol=%.2f fair=%.2f | confirm=%s",
               _Symbol,setup,side,TimeToString(ToEastern(signalTime),TIME_DATE|TIME_MINUTES),
               stopPts,tpPts,vol,fair,tag);
   if(g_audit!=INVALID_HANDLE)
     {
      FileWrite(g_audit,TimeToString(signalTime,TIME_DATE|TIME_MINUTES),
                TimeToString(ToEastern(signalTime),TIME_DATE|TIME_MINUTES),
                key,setup,side,tag,DoubleToString(fair,_Digits),
                DoubleToString(fillReference,_Digits),stopPts,tpPts,vol);
      FileFlush(g_audit);
     }
  }

bool SubmitTrade(const int mode,const int dir,const int key,const double fair,
                 const MqlRates &opening,const datetime signalTime,const string tag)
  {
   double stopPts=0.0,tpPts=0.0,vol=0.0;
   if(!DetermineBracket(opening,InpVolumeContracts,stopPts,tpPts,vol))
      return false;
   if(!VolumeAllowed(vol))
     {
      PrintFormat("JJ skipped order: volume %.3f is invalid for custom symbol. Check tick/contract settings.",vol);
      return false;
     }
   MqlTick tick;
   if(!SymbolInfoTick(_Symbol,tick)) return false;
   double px=(dir>0)?tick.ask:tick.bid;
   if(px<=0.0) return false;
   double sl=RoundTick(px-dir*stopPts);
   double tp=RoundTick(px+dir*tpPts);
   if(InpRequireFairBeyondTP && mode==2)
     {
      if(dir>0 && fair<tp) return false;
      if(dir<0 && fair>tp) return false;
     }
   string comment=(mode==1?"JJ-C-":"JJ-R-")+tag;
   bool submitted=g_trade.PositionOpen(_Symbol,dir>0?ORDER_TYPE_BUY:ORDER_TYPE_SELL,
                                        vol,px,sl,tp,comment);
   uint rc=g_trade.ResultRetcode();
   if(!submitted || (rc!=TRADE_RETCODE_DONE && rc!=TRADE_RETCODE_DONE_PARTIAL))
     {
      PrintFormat("JJ order rejected: retcode=%u, reason=%s",rc,g_trade.ResultRetcodeDescription());
      return false;
     }
   g_liveSetup=mode;
   g_liveSessionKey=key;
   if(mode==1) g_continuationCount++;
   EmitSignal(mode,dir,key,fair,stopPts,tpPts,vol,px,tag,signalTime);
   return true;
  }

int OnInit()
  {
   if(InpTesterOnly && !MQLInfoInteger(MQL_TESTER))
     {
      Print("JJ research EA is TESTER-ONLY by default. Do not deploy without independent review.");
      return INIT_FAILED;
     }
   if(_Period!=PERIOD_M1)
     {
      Print("JJ research EA requires M1 Strategy Tester period.");
      return INIT_PARAMETERS_INCORRECT;
     }
   if(InpVolumeContracts<=0 || InpSwingWidth<1 || InpBOSLookback<5 ||
      InpContinuationMinutes<1 || InpContinuationMinutes>30 ||
      InpMaxRevConsecLosses<1 || InpMaxCounterwickFraction<0 || InpMaxCounterwickFraction>1)
      return INIT_PARAMETERS_INCORRECT;
   g_trade.SetExpertMagicNumber(InpMagic);
   g_trade.SetDeviationInPoints(InpDeviationPoints);
   g_trade.SetTypeFillingBySymbol(_Symbol);
   if(InpStopModel==JJ_ATR_TIERS_1P5R)
     {
      g_atrHandle=iATR(_Symbol,PERIOD_M1,InpATRPeriod);
      if(g_atrHandle==INVALID_HANDLE) return INIT_FAILED;
     }
   if(InpExportSignalAuditCSV)
     {
      // Tester-local audit; avoids unsafe shared-file writes in parallel optimizations.
      string filename="JJ_Simon_Signals_"+IntegerToString((long)InpMagic)+".csv";
      g_audit=FileOpen(filename,FILE_WRITE|FILE_CSV|FILE_ANSI,',');
      if(g_audit!=INVALID_HANDLE)
         FileWrite(g_audit,"DATA_TIME","EASTERN_TIME","SESSION_KEY","SETUP","SIDE",
                   "CONFIRMATION","FAIR_REFERENCE","REFERENCE_FILL","STOP_POINTS","TP_POINTS","CONTRACTS");
      else Print("JJ warning: Signal CSV could not be opened, trade operation continues.");
     }
   Print("JJ Research EA v1 loaded. Rules are a documented mechanical subset, NOT official JJ software.");
   Print("NQ spec check: tick_size=",SymbolInfoDouble(_Symbol,SYMBOL_TRADE_TICK_SIZE),
         " tick_value=",SymbolInfoDouble(_Symbol,SYMBOL_TRADE_TICK_VALUE),
         " volume_step=",SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_STEP));
   return INIT_SUCCEEDED;
  }

void OnDeinit(const int reason)
  {
   if(g_atrHandle!=INVALID_HANDLE) IndicatorRelease(g_atrHandle);
   if(g_audit!=INVALID_HANDLE) FileClose(g_audit);
  }

void OnTick()
  {
   datetime now=iTime(_Symbol,PERIOD_M1,0);
   if(now<=0 || now==g_lastBar) return;
   g_lastBar=now;

   int start=0,finish=0,slot=0,key=0;
   if(!SessionForBar(now,start,finish,slot,key)) return;

   if(key!=g_sessionKey)
     {
      g_sessionKey=key;
      g_continuationCount=0;
      g_reversionConsecLosses=0;
     }

   const int barsNeeded=(int)MathMax(190.0,(double)(InpBOSLookback+2*InpSwingWidth+10));
   MqlRates bars[];
   ArraySetAsSeries(bars,true);
   int n=CopyRates(_Symbol,PERIOD_M1,0,barsNeeded,bars);
   if(n<InpBOSLookback+2*InpSwingWidth+5 || n<5) return;
   if(bars[0].time!=now || bars[0].time-bars[1].time!=60) return;

   int op=-1;
   if(!FindOpeningBar(bars,n,key,start,op)) return;
   if(op<1) return; // Open candle has not CLOSED yet.

   const MqlRates opening=bars[op];
   const double fair=opening.open;
   const int openDirection=IsUp(opening)?1:(IsDown(opening)?-1:0);
   if(openDirection==0) return;

   MqlDateTime closedET;
   TimeToStruct(ToEastern(bars[1].time),closedET);
   int closedMinute=MinuteOfDay(closedET);
   if(closedMinute<start || closedMinute>=finish) return;
   int offset=closedMinute-start;

   if(HasPositionOnSymbol()) return; // Never layer multiple same-symbol positions.

   if(InpTradeContinuation && g_continuationCount==0 &&
      offset<InpContinuationMinutes && (InpAllowOpeningBarSignal || offset>0))
     {
      int dir=openDirection;
      if((dir>0 && IsUp(bars[1])) || (dir<0 && IsDown(bars[1])))
        {
         string tag;
         if(ConfirmedEntry(bars,n,dir,tag))
           {
            SubmitTrade(1,dir,key,fair,opening,bars[1].time,tag);
            return;
           }
        }
     }

   if(!InpTradeReversion || offset<InpContinuationMinutes ||
      g_reversionConsecLosses>=InpMaxRevConsecLosses)
      return;

   // Reversion only after price is on the opposite side of the fair reference.
   // This guards against an accidental trend-following trade mislabelled as reversion.
   int direction=0;
   if(bars[1].close>fair) direction=-1;
   if(bars[1].close<fair) direction=1;
   if(direction==0) return;
   string tag;
   if(!ConfirmedEntry(bars,n,direction,tag)) return;
   SubmitTrade(2,direction,key,fair,opening,bars[1].time,tag);
  }

void OnTradeTransaction(const MqlTradeTransaction &trans,
                        const MqlTradeRequest &request,
                        const MqlTradeResult &result)
  {
   if(trans.type!=TRADE_TRANSACTION_DEAL_ADD || trans.deal==0) return;
   if(!HistoryDealSelect(trans.deal)) return;
   if((ulong)HistoryDealGetInteger(trans.deal,DEAL_MAGIC)!=InpMagic) return;
   if(HistoryDealGetString(trans.deal,DEAL_SYMBOL)!=_Symbol) return;
   ENUM_DEAL_ENTRY side=(ENUM_DEAL_ENTRY)HistoryDealGetInteger(trans.deal,DEAL_ENTRY);
   if(side==DEAL_ENTRY_IN)
     {
      g_openEntryCosts+=HistoryDealGetDouble(trans.deal,DEAL_COMMISSION)
                       +HistoryDealGetDouble(trans.deal,DEAL_FEE);
      return;
     }
   if(side!=DEAL_ENTRY_OUT && side!=DEAL_ENTRY_OUT_BY && side!=DEAL_ENTRY_INOUT) return;

   // Include accumulated entry-side charges when counting consecutive losses.
   double realised=HistoryDealGetDouble(trans.deal,DEAL_PROFIT)
                  +HistoryDealGetDouble(trans.deal,DEAL_COMMISSION)
                  +HistoryDealGetDouble(trans.deal,DEAL_SWAP)
                  +HistoryDealGetDouble(trans.deal,DEAL_FEE)
                  +g_openEntryCosts;
   g_openEntryCosts=0.0;
   if(g_liveSetup==2 && g_liveSessionKey==g_sessionKey)
     {
      if(realised<0.0) g_reversionConsecLosses++;
      else if(realised>0.0) g_reversionConsecLosses=0;
      PrintFormat("JJ reversion completed: PnL=%.2f consecutive session losses=%d",
                  realised,g_reversionConsecLosses);
     }
   g_liveSetup=0;
   g_liveSessionKey=-1;
  }