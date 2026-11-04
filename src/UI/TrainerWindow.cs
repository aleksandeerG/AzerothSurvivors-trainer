using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace AzerothTrainer
{
    sealed class TrainerWindow : Form
    {
        readonly NumericUpDown spell=new NumericUpDown(),item=new NumericUpDown(),cost=new NumericUpDown(),speed=new NumericUpDown(),xp=new NumericUpDown(),pickDelay=new NumericUpDown(),resource=new NumericUpDown(),advance=new NumericUpDown(),talents=new NumericUpDown(),marks=new NumericUpDown();
        readonly CheckBox spellOn=new CheckBox(),itemOn=new CheckBox(),fixedCost=new CheckBox(),unlimitedBan=new CheckBox(),unlimitedReroll=new CheckBox(),autoHeal=new CheckBox(),speedOn=new CheckBox(),xpOn=new CheckBox(),autoSpell=new CheckBox(),autoItem=new CheckBox();
        readonly ComboBox resourceKind=new ComboBox(),classKind=new ComboBox(),spellRule=new ComboBox(),itemRule=new ComboBox();
        readonly DataGridView stats=new DataGridView();
        readonly Label status=new Label();readonly Timer timer=new Timer();
        readonly Button enable=new Button(),apply=new Button(),disable=new Button();
        readonly List<Control> connectedControls=new List<Control>();
        readonly int[] classIds={1,2,3,4,5,7,8,9,11};
        PatchSession session;bool busy;uint talentPending,marksPending;
        internal TrainerWindow()
        {
            Text="Azeroth Survivors Trainer Plus";ClientSize=new Size(760,690);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;
            Font=new Font("Segoe UI",10);BackColor=Color.FromArgb(24,27,34);ForeColor=Color.WhiteSmoke;
            Label(Controls,"Azeroth Survivors Trainer",22,17,700,38,new Font("Segoe UI",18,FontStyle.Bold));
            var tabs=new TabControl{Location=new Point(20,70),Size=new Size(720,410)};Controls.Add(tabs);
            var rarity=Tab(tabs,"Rarity and rolls");var run=Tab(tabs,"Run and time");var profile=Tab(tabs,"Talents and marks");var automation=Tab(tabs,"Auto-pick");var statTab=Tab(tabs,"Run stats");
            Check(rarity.Controls,spellOn,"Override spell legendary chance",18,18,480,false);Number(rarity.Controls,spell,538,18,120,0,100,50,1,0.1m);Label(rarity.Controls,"%",665,21,20,25,null);
            Check(rarity.Controls,itemOn,"Override in-run item legendary chance",18,66,480,false);Number(rarity.Controls,item,538,66,120,0,100,50,1,0.1m);Label(rarity.Controls,"%",665,69,20,25,null);
            Label(rarity.Controls,"Chance per card. Items: rings, trinkets and amulets.",18,113,660,42,null);
            Check(rarity.Controls,fixedCost,"Fixed paid reroll cost (0 = free)",18,172,480,false);Number(rarity.Controls,cost,538,172,120,0,100000,100,0,1);
            Check(rarity.Controls,unlimitedBan,"Unlimited banishes",18,227,330,false);Check(rarity.Controls,unlimitedReroll,"Unlimited free rerolls",365,227,330,false);
            Label(rarity.Controls,"Affects future run offers. Click Apply options after changes.",18,293,660,70,null);
            Check(run.Controls,speedOn,"Simulation speed",18,15,220,false);Number(run.Controls,speed,235,15,115,0,10,1,1,0.1m);Label(run.Controls,"x  (0 freezes time; 1 is normal)",365,19,320,28,null);
            Check(run.Controls,xpOn,"Run XP multiplier",18,60,220,false);Number(run.Controls,xp,235,60,115,0.1m,100,1,1,0.1m);
            Check(run.Controls,autoHeal,"Auto-heal while alive",388,60,300,false);
            Label(run.Controls,"Changes apply during a run. Stat minimums are in Run stats.",18,115,660,50,null);
            Label(run.Controls,"Run resource",18,202,180,24,null);resourceKind.SetBounds(18,231,220,30);ComboStyle(resourceKind);
            resourceKind.Items.AddRange(new object[]{"Gold (set)","Banishes (set)","Free rerolls (set)","Shield (set)","Run XP (grant)"});run.Controls.Add(resourceKind);
            Number(run.Controls,resource,252,231,135,0,100000,1000,0,1);resourceKind.SelectedIndexChanged+=delegate{ResourceLimits();};resourceKind.SelectedIndex=0;
            Button(run.Controls,"Set / grant",407,228,130,SetResource,true);Button(run.Controls,"Heal now",551,228,130,delegate{session.Heal();Queued("Healing");},true);
            Number(run.Controls,advance,18,293,150,0,3600,60,0,1);Button(run.Controls,"Advance clock (seconds)",184,290,235,delegate{session.Advance((float)advance.Value);Queued("Clock advance");},true);
            Label(run.Controls,"High speeds can skip collisions. Clock advances bring spawns forward.",18,341,660,45,new Font("Segoe UI",9));
            Label(profile.Controls,"Class to edit",18,21,210,27,null);classKind.SetBounds(235,19,280,30);ComboStyle(classKind);
            classKind.Items.AddRange(new object[]{"Warrior","Paladin","Hunter","Rogue","Priest","Shaman","Mage","Warlock","Druid"});classKind.SelectedIndex=0;profile.Controls.Add(classKind);
            Label(profile.Controls,"Earned talent points",18,83,250,27,null);Number(profile.Controls,talents,281,80,125,0,51,51,0,1);
            Button(profile.Controls,"Set talent points",430,77,250,delegate{talentPending=session.SetTalentPoints(classIds[classKind.SelectedIndex],(int)talents.Value);Queued("Class talent points");},true);
            Label(profile.Controls,"Class marks",18,143,250,27,null);Number(profile.Controls,marks,281,140,125,0,1000000,1000,1,0.1m);
            Button(profile.Controls,"Set marks",430,137,250,delegate{marksPending=session.SetMarks(classIds[classKind.SelectedIndex],(int)(marks.Value*10));Queued("Class marks");},true);
            Label(profile.Controls,"Permanent changes; backups are saved in trainer\\backups.\nRespec before lowering points. Reopen the Armory to refresh.\nMarks still require normal unlocks.",18,206,660,156,new Font("Segoe UI",9));
            Check(automation.Controls,autoSpell,"Auto-pick spell offers",18,21,290,false);
            spellRule.SetBounds(333,21,345,30);ComboStyle(spellRule);spellRule.Items.AddRange(AutoPickCode.Rules);automation.Controls.Add(spellRule);spellRule.SelectedIndex=4;
            Check(automation.Controls,autoItem,"Auto-pick in-run item offers",18,78,310,false);
            itemRule.SetBounds(333,78,345,30);ComboStyle(itemRule);itemRule.Items.AddRange(AutoPickCode.Rules);automation.Controls.Add(itemRule);itemRule.SelectedIndex=4;
            Label(automation.Controls,"Wait before each automatic pick",18,147,340,28,null);Number(automation.Controls,pickDelay,398,144,130,0,10,0.5m,1,0.1m);Label(automation.Controls,"seconds",543,147,130,28,null);
            Label(automation.Controls,"Picks can learn spells or replace gear. Ties choose the leftmost card.\nMissing slots use the last card. Delay uses real time.\nClick Apply options after changes.",18,208,660,163,new Font("Segoe UI",9));
            BuildStats(statTab);
            BindSwitch(spellOn,spell);BindSwitch(itemOn,item);BindSwitch(fixedCost,cost);BindSwitch(speedOn,speed);BindSwitch(xpOn,xp);BindSwitch(autoSpell,spellRule);BindSwitch(autoItem,itemRule);
            Button(Controls,"Launch game",20,497,160,LaunchGame,false);
            Configure(enable,"Enable trainer",194,497,170);enable.Click+=async delegate{await ApplyAsync();};Controls.Add(enable);
            Configure(apply,"Apply options",378,497,170);apply.Click+=async delegate{await ApplyAsync();};Controls.Add(apply);
            Configure(disable,"Disable",562,497,178);disable.Click+=delegate{try{session.Disable();talentPending=marksPending=0;status.ForeColor=Color.WhiteSmoke;UpdateButtons();status.Text="Disabled. Original game code restored.";}catch(Exception ex){ShowError(ex);}};Controls.Add(disable);
            status.SetBounds(23,555,714,74);status.Text="Disabled. Start the game, select options, then enable.";Controls.Add(status);
            Label(Controls,"Created by aleksanderG",23,649,205,25,new Font("Segoe UI",9));
            CreditLink("aleksanderg.com","https://aleksanderg.com/",235,649,190);
            CreditLink("Support on Ko-fi","https://ko-fi.com/K3P227JS3D",451,649,220);
            timer.Interval=700;timer.Tick+=delegate{PollSafely(Poll,delegate(Exception ex){status.Text="Status check: "+ex.Message;});};timer.Start();UpdateButtons();
            FormClosing+=delegate(object sender,FormClosingEventArgs e)
            {
                if(busy){e.Cancel=true;status.Text="Finishing the trainer update. Close again when it completes.";return;}
                try{Detach();timer.Stop();timer.Dispose();}catch(Exception ex){e.Cancel=true;ShowError(ex);}
            };
        }
        static TabPage Tab(TabControl tabs,string title){var p=new TabPage(title){BackColor=Color.FromArgb(30,34,43),ForeColor=Color.WhiteSmoke};tabs.TabPages.Add(p);return p;}
        internal static void PollSafely(Action poll,Action<Exception> report){try{poll();}catch(Exception ex){report(ex);}}
        internal static string FailureStatus(bool active){return active?"Partially enabled; click Apply options to finish, or Disable to restore game code.":"Enable failed. Resolve the error, then try again.";}
        internal static string ActiveSummary(TrainerOptions o)
        {
            var parts=new List<string>();if(o.SpellLegendary)parts.Add("Spells "+o.SpellChance.ToString("0.0")+"%");if(o.ItemLegendary)parts.Add("Items "+o.ItemChance.ToString("0.0")+"%");
            if(o.FixedCost)parts.Add("Reroll "+o.RerollCost+" gold");if(o.UnlimitedBanish)parts.Add("Unlimited banishes");if(o.UnlimitedReroll)parts.Add("Unlimited rerolls");
            if(o.SpeedEnabled)parts.Add("Speed "+o.Speed.ToString("0.0")+"x");if(o.XpEnabled)parts.Add("XP "+o.XpMultiplier.ToString("0.0")+"x");if(o.AutoHeal)parts.Add("Auto-heal");
            if(o.DamageMinimum>0)parts.Add("Damage floor "+o.DamageMinimum.ToString("0.0")+"x");if(o.PickupMinimum>0)parts.Add("Pickup floor "+o.PickupMinimum.ToString("0.0"));
            if(o.AutoSpellMode>0)parts.Add("Auto-pick spells");if(o.AutoItemMode>0)parts.Add("Auto-pick items");int count=0;foreach(bool active in o.StatEnabled)if(active)count++;if(count>0)parts.Add(count+" stat minimums");
            return parts.Count==0?"Connected. All options are off.":"Enabled | "+String.Join(" | ",parts.ToArray());
        }
        async Task ApplyAsync()
        {
            if(busy)return;TrainerOptions options;try{options=Options();}catch(Exception ex){ShowError(ex);return;}busy=true;SetBusy(true);status.Text="Checking and applying trainer options...";
            try
            {
                if(session!=null&&session.Exited){session.Dispose();session=null;}
                if(session==null)session=await Task.Run(delegate{return PatchSession.Attach();});
                await Task.Run(delegate{session.Apply(options);});status.ForeColor=Color.FromArgb(255,192,90);status.Text=ActiveSummary(options);
            }
            catch(Exception ex){status.ForeColor=Color.LightCoral;status.Text=FailureStatus(session!=null&&session.Enabled);ShowError(ex);}
            finally{busy=false;SetBusy(false);}
        }
        internal TrainerOptions Options()
        {
            if(!stats.EndEdit())throw new Exception("Finish editing the stat value before applying options.");
            var result=new TrainerOptions{SpellLegendary=spellOn.Checked,ItemLegendary=itemOn.Checked,SpellChance=(double)spell.Value,ItemChance=(double)item.Value,FixedCost=fixedCost.Checked,RerollCost=(int)cost.Value,UnlimitedBanish=unlimitedBan.Checked,UnlimitedReroll=unlimitedReroll.Checked,SpeedEnabled=speedOn.Checked,XpEnabled=xpOn.Checked,Speed=(double)speed.Value,XpMultiplier=(double)xp.Value,AutoHeal=autoHeal.Checked,AutoSpellMode=autoSpell.Checked?spellRule.SelectedIndex+1:0,AutoItemMode=autoItem.Checked?itemRule.SelectedIndex+1:0,AutoPickDelay=(double)pickDelay.Value};
            for(int i=0;i<RunStat.All.Length;i++){result.StatEnabled[i]=Convert.ToBoolean(stats.Rows[i].Cells[0].Value);result.StatValues[i]=Convert.ToDouble(stats.Rows[i].Cells[2].Value);}
            PatchSession.ValidateOptions(result);return result;
        }
        void BuildStats(TabPage tab)
        {
            Label(tab.Controls,"Set minimums, check the rows, then Apply options. Stronger bonuses stay.",18,12,670,40,new Font("Segoe UI",9));
            stats.SetBounds(18,56,678,270);stats.AllowUserToAddRows=false;stats.AllowUserToDeleteRows=false;stats.AllowUserToResizeRows=false;stats.RowHeadersVisible=false;stats.AutoGenerateColumns=false;
            stats.BackgroundColor=Color.FromArgb(30,34,43);stats.DefaultCellStyle.BackColor=Color.White;stats.DefaultCellStyle.ForeColor=Color.Black;stats.DefaultCellStyle.SelectionBackColor=Color.FromArgb(200,220,240);stats.DefaultCellStyle.SelectionForeColor=Color.Black;
            stats.Columns.Add(new DataGridViewCheckBoxColumn{HeaderText="On",Width=45});stats.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="Stat",Width=255,ReadOnly=true});stats.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="Minimum",Width=130});stats.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="Unit",Width=210,ReadOnly=true});
            foreach(RunStat stat in RunStat.All)stats.Rows.Add(false,stat.Name,stat.Default,stat.Unit);
            stats.CellValidating+=delegate(object sender,DataGridViewCellValidatingEventArgs e)
            {
                if(e.ColumnIndex!=2||e.RowIndex<0)return;double value;var stat=RunStat.All[e.RowIndex];
                try{if(!Double.TryParse(Convert.ToString(e.FormattedValue),out value))throw new Exception();stat.Bits(value);stats.Rows[e.RowIndex].ErrorText="";}
                catch{e.Cancel=true;stats.Rows[e.RowIndex].ErrorText="Enter "+stat.Min+" to "+stat.Max+(stat.Kind>=4?", whole numbers only.":".");}
            };
            tab.Controls.Add(stats);
            Label(tab.Controls,"Health sets max HP; Heal now refills it. DoT stacks: 1-4.\nDisable stops enforcement; existing stats may remain.",18,337,665,55,new Font("Segoe UI",9));
        }
        static void BindSwitch(CheckBox box,Control input){input.Enabled=box.Checked;box.CheckedChanged+=delegate{input.Enabled=box.Checked;};}
        static void ComboStyle(ComboBox box)
        {
            box.DropDownStyle=ComboBoxStyle.DropDownList;box.ForeColor=Color.Black;box.BackColor=Color.White;box.DrawMode=DrawMode.OwnerDrawFixed;box.ItemHeight=22;
            box.DrawItem+=delegate(object sender,DrawItemEventArgs e){e.DrawBackground();int index=e.Index>=0?e.Index:box.SelectedIndex;if(index>=0)TextRenderer.DrawText(e.Graphics,box.GetItemText(box.Items[index]),box.Font,e.Bounds,box.Enabled?Color.Black:SystemColors.GrayText,TextFormatFlags.Left|TextFormatFlags.VerticalCenter);e.DrawFocusRectangle();};
        }
        void Poll()
        {
            if(busy||session==null)return;if(session.Exited){Detach();status.Text="Game closed. Trainer detached.";return;}
            if(talentPending!=0)
            {
                int code=session.TalentResult(talentPending);if(code!=0){talentPending=0;status.Text=code==1?"Class talent points updated. Reopen the Armory to refresh.":(code==2?"Talent change refused: respec this class before lowering below its allocated points.":"Talent change failed: class record unavailable.");}
            }
            if(marksPending!=0&&session.MarksResult(marksPending)==1){marksPending=0;status.Text="Class marks updated. Reopen the Armory to refresh.";}
        }
        void ResourceLimits(){resource.DecimalPlaces=resourceKind.SelectedIndex==3?1:0;resource.Maximum=resourceKind.SelectedIndex==0?100000:(resourceKind.SelectedIndex==1||resourceKind.SelectedIndex==2?9999:1000000);}
        void SetResource()
        {
            switch(resourceKind.SelectedIndex){case 0:session.SetGold((int)resource.Value);break;case 1:session.SetBanishes((int)resource.Value);break;case 2:session.SetFreeRerolls((int)resource.Value);break;case 3:session.SetShield((float)resource.Value);break;case 4:session.GrantXp((int)resource.Value);break;}Queued(resourceKind.Text);
        }
        void Queued(string what){status.Text=what+" queued for the next run update.";}
        void LaunchGame()
        {
            using(Process running=GameLocator.FindRunning())
            {
                if(running!=null){GameLocator.Remember(running.MainModule.FileName);status.Text="Game is running. Enable the trainer when ready.";return;}
            }
            string gamePath=GameLocator.FindInstalled();
            while(!File.Exists(gamePath))
            {
                using(var picker=new FolderBrowserDialog{Description="Choose the folder containing AzerothSurvivors.exe",ShowNewFolderButton=false})
                {
                    if(picker.ShowDialog(this)!=DialogResult.OK){status.Text="Start the game yourself, or click Launch game to choose its folder.";return;}
                    gamePath=Path.Combine(picker.SelectedPath,GameLocator.ExecutableName);
                }
                if(!File.Exists(gamePath))MessageBox.Show(this,"AzerothSurvivors.exe was not found. Choose the game's installation folder.","Game folder",MessageBoxButtons.OK,MessageBoxIcon.Information);
            }
            using(Process p=Process.Start(new ProcessStartInfo(gamePath){WorkingDirectory=Path.GetDirectoryName(gamePath),UseShellExecute=true})){}
            GameLocator.Remember(gamePath);status.Text="Game started. Enable the trainer when ready.";
        }
        void Detach(){if(session!=null){session.Dispose();session=null;}talentPending=marksPending=0;status.ForeColor=Color.WhiteSmoke;UpdateButtons();}
        void UpdateButtons(){bool active=session!=null&&session.Enabled;enable.Enabled=!active&&!busy;apply.Enabled=disable.Enabled=active&&!busy;foreach(Control c in connectedControls)c.Enabled=active&&!busy;}
        void SetBusy(bool value){foreach(Control c in Controls)if(!(c is Label))c.Enabled=!value;UpdateButtons();}
        void ShowError(Exception ex){UpdateButtons();MessageBox.Show(this,ex.Message,"Trainer",MessageBoxButtons.OK,MessageBoxIcon.Information);}
        void CreditLink(string text,string url,int x,int y,int width)
        {
            var link=new LinkLabel{Text=text,Location=new Point(x,y),Size=new Size(width,25),LinkColor=Color.FromArgb(130,195,255),ActiveLinkColor=Color.White,VisitedLinkColor=Color.FromArgb(130,195,255)};
            link.LinkClicked+=delegate{try{using(Process p=Process.Start(new ProcessStartInfo(url){UseShellExecute=true})){} }catch(Exception ex){ShowError(ex);}};
            Controls.Add(link);
        }
        void Number(Control.ControlCollection c,NumericUpDown n,int x,int y,int width,decimal min,decimal max,decimal value,int decimals,decimal step){n.SetBounds(x,y,width,30);n.Minimum=min;n.Maximum=max;n.DecimalPlaces=decimals;n.Increment=step;n.Value=value;c.Add(n);}
        void Check(Control.ControlCollection c,CheckBox b,string text,int x,int y,int width,bool value){b.Text=text;b.SetBounds(x,y,width,29);b.Checked=value;c.Add(b);}
        void Label(Control.ControlCollection c,string text,int x,int y,int width,int height,Font font){var l=new Label{Text=text,Location=new Point(x,y),Size=new Size(width,height)};if(font!=null)l.Font=font;c.Add(l);}
        static void Configure(Button b,string text,int x,int y,int width){b.Text=text;b.SetBounds(x,y,width,36);b.FlatStyle=FlatStyle.Flat;b.BackColor=Color.FromArgb(43,48,59);}
        void Button(Control.ControlCollection c,string text,int x,int y,int width,Action action,bool needsSession)
        {var b=new Button();Configure(b,text,x,y,width);b.Click+=delegate{try{action();}catch(Exception ex){status.Text="Action failed: "+ex.Message;ShowError(ex);}};c.Add(b);if(needsSession)connectedControls.Add(b);}
    }
}
