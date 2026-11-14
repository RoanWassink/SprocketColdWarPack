from pathlib import Path
import subprocess
subprocess.run(['powershell','-NoProfile','-File',str(Path(__file__).with_suffix('.ps1'))],check=True)
