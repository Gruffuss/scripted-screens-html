-- HtmlEmpty.lua -- a blank surface, for isolating frame-rate questions.
local ui = ss.ui.surface("main")
ss.ui.activate("main")
ui:clear()
ui:commit()
