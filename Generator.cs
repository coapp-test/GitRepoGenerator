using System;
using System.IO;
using System.Xml;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace GitRepoGenerator
{
    class Generator
    {

        private const string HelpMessage =
    "\nUsage:  GitRepoGenerator --url=<API_Url_Location> --existing=<Existing_Projects_File> [--disabled] [--ignore=<Project_Name>]" +
    "\n\n--url=\t\tThe full url for the GitHub API call.\n" +
    "--existing=\tInput text file listing existing Jenkins projects, 1 project per line.\n" +
    "--disabled\tOptional parameter.  Will generate Jenkins project files already set to 'Disabled'.\n"+
    "--ignore=\tOptional parameter.  <Project_Name> will be removed from <Existing_Projects_File> before processing.\n";


        /*
         * 
         * 
         * Only input is a string url to the GitHub Rest API.
         * Writes Jenkins project xml files to current directory.
         * Writes to standard output a list of all repos with project files.
         */
        static int Main(string[] args)
        {
            bool EnableProjects = true;
            
            args.Switches();
            if (args.SwitchValue("help") != null)
            {
                Console.WriteLine(HelpMessage);
                return -1;
            }

            // Check for --disabled switch
            if (args.SwitchValue("disabled") != null)
                EnableProjects = false;

            string ignore = args.SwitchValue("ignore") ?? String.Empty;

            // Load the list of existing projects...
            List<string> existing;
            if (args.SwitchValue("existing") != null)
                try
                {
                    //FileStream file = new FileStream(,FileMode.Open,FileAccess.Read);
                    StreamReader IN = new StreamReader(args.SwitchValue("existing"));
                    List<string> init = new List<string>();
                    while (!IN.EndOfStream)
                    {
                        init.Add(IN.ReadLine());
                    }
                    existing = new List<string>(init.Distinct());
                    existing.Remove(ignore); //remove the project to be ignored
                }
                catch (FileNotFoundException e)
                {
                    Console.WriteLine(HelpMessage);
                    return -2;
                }
            else
            {
                Console.WriteLine(HelpMessage);
                return -2;
            }

            // Load the repo list...
            List<JObject> jsons = new List<JObject>();
            if (args.SwitchValue("url") != null)
                try
                {
                    string input = new System.Net.WebClient().DownloadString(args.SwitchValue("url"));

                    List<string> set = new List<string>();
                    StringReader reader = new StringReader(input);
                    char c = (char)reader.Read(); // I expect this to be '['
                    string s = String.Empty;
                    int depth = 0;
                    c = (char)reader.Read(); // I need this to not be the initial '['
                    while (c != ']' || depth > 0) // escape when we hit the end
                    {
                        switch (c)
                        {
                            case '{':
                                depth += 1;
                                goto default;
                            case '}':
                                depth -= 1;
                                goto default;
                            case ',':
                                if (depth == 0)
                                {
                                    jsons.Add(JObject.Parse(s));
                                    s = String.Empty;
                                }
                                else
                                {
                                    goto default;
                                }
                                break;
                            default:
                                s += c;
                                break;
                        }
                        c = (char)reader.Read();
                    }
                    jsons.Add(JObject.Parse(s));
                }
                catch (Exception e)
                {
                    Console.WriteLine(HelpMessage);
                    return -3;                
                }
            else
            {
                Console.WriteLine(HelpMessage);
                return -3;
            }

            // Do actual work...
            Dictionary<string,JObject> repos = new Dictionary<string, JObject>();
            foreach (JObject R in jsons)
            {
                try
                {
                    repos.Add((string) R["name"], R);
                }
                catch (Exception e)
                {
                    
                }
                    
            }

            // Compare existing with new list
            List<string> stillExisting = new List<string>();
            List<string> newRepos = new List<string>();

            foreach (KeyValuePair<string, JObject> repo in repos)
            {
                if (existing.Contains(repo.Key))
                {
                    stillExisting.Add(repo.Key);
                    existing.Remove(repo.Key);
                }
                else
                {
                    if (Generate(repo.Value, EnableProjects))
                        newRepos.Add(repo.Key);
                    else
                    {
                        Console.Error.WriteLine("Error generating project file for repo: "+repo.Key);
                    }
                }
            }
            
            //// Generate text files with repo/file names

            //List of projects to disable
            StreamWriter disable = new StreamWriter("disable.txt", false);
            foreach (string s in existing)
            {
                disable.WriteLine(s);
            }
            disable.Close();

            //List of active/existing repos
            StreamWriter enable = new StreamWriter("enable.txt", false);
            foreach (string s in stillExisting)
            {
                enable.WriteLine(s);
            }
            enable.Close();

            //List of new repo project files
            StreamWriter newProjects = new StreamWriter("new.txt", false);
            foreach (string s in newRepos)
            {
                newProjects.WriteLine(s);
            }
            newProjects.Close();

            // If we've made it this far, exit with success.
            return 0;
        }

        private static bool Generate(JObject repo, bool enabled = true)
        {
            try
            {
                XmlWriterSettings init = new XmlWriterSettings();
                init.NewLineChars = "\n";
                init.Indent = true;
                XmlWriter xml = XmlWriter.Create(((string) repo["name"] + ".config.xml"), init);
                
                xml.WriteStartElement("project");
                    xml.WriteStartElement("actions");
                    xml.WriteEndElement(); //empty actions
                    xml.WriteElementString("description","Auto-Build project for github repository at "+(string)repo["html_url"]);
                    xml.WriteElementString("keepDependencies","false");
                    xml.WriteStartElement("properties");
                        xml.WriteStartElement("com.coravy.hudson.plugins.github.GithubProjectProperty");
                            xml.WriteElementString("projectUrl", (string)repo["html_url"]);
                        xml.WriteEndElement(); //end GithubProjectProperty
                    xml.WriteEndElement(); //end properties
                    xml.WriteStartElement("scm");
                        xml.WriteAttributeString("class","hudson.plugins.git.GitSCM");
                        xml.WriteElementString("configVersion","2");
                        xml.WriteStartElement("userRemoteConfigs");
                            xml.WriteStartElement("hudson.plugins.git.UserRemoteConfig");
                                xml.WriteStartElement("name");
                                xml.WriteFullEndElement();
                                xml.WriteStartElement("refspec");
                                xml.WriteFullEndElement();
                                xml.WriteElementString("url", (string)repo["git_url"]);
                            xml.WriteEndElement(); //end hudson...UserRemoteConfig
                        xml.WriteEndElement(); //end userRemoteConfigs
                        xml.WriteStartElement("branches");
                            xml.WriteStartElement("hudson.plugins.git.BranchSpec");
                                xml.WriteElementString("name","**");
                            xml.WriteEndElement(); //end hudson...BranchSpec
                        xml.WriteEndElement(); //end branches
                        xml.WriteElementString("disableSubmodules","false");
                        xml.WriteElementString("recursiveSubmodules","true");
                        xml.WriteElementString("doGenerateSubmoduleConfigurations","false");
                        xml.WriteElementString("authorOrCommitter","false");
                        xml.WriteElementString("clean","true");
                        xml.WriteElementString("wipeOutWorkspace","true");
                        xml.WriteElementString("pruneBranches","false");
                        xml.WriteElementString("remotePoll","false");
                        xml.WriteStartElement("buildChooser");
                            xml.WriteAttributeString("class","hudson.plugins.git.util.DefaultBuildChooser");
                        xml.WriteEndElement(); //end buildChooser
                        xml.WriteElementString("gitTool","Default");
                        xml.WriteStartElement("submoduleCfg");
                            xml.WriteAttributeString("class", "list");
                        xml.WriteEndElement(); //end submoduleCfg
                        xml.WriteStartElement("relativeTargetDir");
                        xml.WriteFullEndElement();
                        xml.WriteStartElement("reference");
                        xml.WriteFullEndElement();
                        xml.WriteStartElement("excludedRegions");
                        xml.WriteFullEndElement();
                        xml.WriteStartElement("excludedUsers");
                        xml.WriteFullEndElement();
                        xml.WriteStartElement("gitConfigName");
                        xml.WriteFullEndElement();
                        xml.WriteStartElement("gitConfigEmail");
                        xml.WriteFullEndElement();
                        xml.WriteElementString("skipTag", "false");
                        xml.WriteStartElement("scmName");
                        xml.WriteFullEndElement();
                    xml.WriteEndElement(); //end scm
                    xml.WriteElementString("quietPeriod","30");
                    xml.WriteElementString("assignedNode","master");
                    xml.WriteElementString("canRoam", "false");
                    xml.WriteElementString("disabled",enabled?"true":"false");
                    xml.WriteElementString("blockBuildWhenDownstreamBuilding","false");
                    xml.WriteElementString("blockBuildWhenUpstreamBuilding", "false");
                    xml.WriteStartElement("triggers");
                        xml.WriteAttributeString("class","vector");
                        xml.WriteStartElement("hudson.triggers.SCMTrigger");
                            xml.WriteElementString("spec",@"0 * * * *
20 * * * *
40 * * * *");
                        xml.WriteEndElement(); //end SCMTrigger
                    xml.WriteEndElement(); //end triggers
                    xml.WriteElementString("concurrentBuild","false");
                    xml.WriteStartElement("builders");
                        xml.WriteStartElement("hudson.tasks.BatchFile");
                            xml.WriteElementString("command", @"ptk test");
                        xml.WriteEndElement(); //end BatchFile
                        xml.WriteStartElement("hudson.tasks.BatchFile");
                            xml.WriteElementString("command", @"ptk package");
                        xml.WriteEndElement(); //end BatchFile
                    xml.WriteEndElement(); //end builders
                    xml.WriteStartElement("publishers");
                        xml.WriteStartElement("hudson.tasks.ArtifactArchiver");
                            xml.WriteElementString("artifacts",@".\COPKG\**.msi");
                            xml.WriteElementString("latestOnly","false");
                        xml.WriteEndElement(); //end ArtifactArchiver
                    xml.WriteEndElement(); //end publishers
                    xml.WriteStartElement("buildWrappers");
                    xml.WriteEndElement();
                xml.WriteEndElement(); //end project

                //close and write the xml file
                xml.Close();

                return true;
            }
            catch(Exception e)
            {
                Console.Error.WriteLine(e.Message);
                Console.Error.WriteLine(e.StackTrace);
            }
            return false;
        }
    }
}
