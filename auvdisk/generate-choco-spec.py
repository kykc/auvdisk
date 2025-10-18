import xml.etree.ElementTree as ET
import sys
import os.path;

tpl = ""

with open('choco.tpl') as f:
    tpl = f.read()

tree = ET.parse('auvdisk.csproj')
root = tree.getroot()

projectVariables = {}

for child in root:
    if child.tag == "PropertyGroup":
        for property in child:
            projectVariables[property.tag] = property.text

variables = {}

variables["AUTOMATL_ID"] = projectVariables["AssemblyName"] + ".portable"
variables["AUTOMATL_VERSION"] = projectVariables["Version"]
variables["AUTOMATL_TITLE"] = projectVariables["AssemblyName"]
variables["AUTOMATL_AUTHORS_COMMA_SEPARATED"] = projectVariables["Authors"]
variables["AUTOMATL_TAGS_SPACE_SEPARATED"] = ""
variables["AUTOMATL_URL"] = "https://github.com/kykc/auvdisk"
variables["AUTOMATL_SUMMARY"] = projectVariables["AssemblyName"]
variables["AUTOMATL_DESCRIPTION_MARKDOWN"] = projectVariables["AssemblyName"]
variables["AUTOMATL_TARGET_DIR"] = sys.argv[-1]

nuspec_text = tpl.format(**variables)
print(nuspec_text)

with open('auvdisk.choco.nuspec', 'w') as f:
    f.write(nuspec_text)

# TODO: mask all files besides target exe?
with open(os.path.join(variables["AUTOMATL_TARGET_DIR"], "createdump.exe.ignore"), 'w') as f:
    f.write("")

