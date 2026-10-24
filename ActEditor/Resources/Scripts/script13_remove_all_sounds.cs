using System;
using ErrorManager;
using GRF.FileFormats.ActFormat;

namespace Scripts {
	public class Script : IActScript {
		public object DisplayName {
			get { return "Remove all sounds"; }
		}
		
		public string Group {
			get { return "Scripts"; }
		}
		
		public string InputGesture {
			get { return null; }
		}
		
		public string Image {
			get { return "soundOff.png"; }
		}
		
		public void Execute(Act act, int selectedActionIndex, int selectedFrameIndex, int[] selectedLayerIndexes) {
			if (act == null) return;
			
			int soundCount = act.SoundFiles.Count;
			int frameCount = 0;
			
			try {
				act.Commands.Begin();
				act.Commands.Backup(actInput => {
					actInput.SoundFiles.Clear();
					
					foreach (Frame frame in actInput.GetAllFrames()) {
						if (frame.SoundId != -1) {
							frame.SoundId = -1;
							frameCount++;
						}
					}
				}, "Remove all sounds", true);
			}
			catch (Exception err) {
				act.Commands.CancelEdit();
				ErrorHandler.HandleException(err, ErrorLevel.Warning);
			}
			finally {
				act.Commands.End();
				act.InvalidateVisual();
				
				if (soundCount == 0 && frameCount == 0) {
					ErrorHandler.HandleException("No sounds were found.", ErrorLevel.NotSpecified);
				}
				else {
					ErrorHandler.HandleException("Removed " + soundCount + " sound file(s) and cleared " + frameCount + " frame sound reference(s).", ErrorLevel.NotSpecified);
				}
			}
		}
		
		public bool CanExecute(Act act, int selectedActionIndex, int selectedFrameIndex, int[] selectedLayerIndexes) {
			return act != null;
		}
	}
}
