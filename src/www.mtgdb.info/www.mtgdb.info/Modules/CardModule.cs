using System;
using SuperSimple.Auth;
using Nancy;
using Nancy.Security;
using System.Linq;
using MtgDb.Info.Driver;
using System.Collections.Generic;
using System.Configuration;
using Nancy.ModelBinding;
using System.Reflection;
using Nancy.Validation;

namespace MtgDb.Info
{
    public class CardModule : NancyModule
    {
        public IRepository repository = 
            new MongoRepository (ConfigurationManager.AppSettings.Get("db"));

        public Db magicdb = 
            new Db (ConfigurationManager.AppSettings.Get("api"));

        public CardModule ()
        {
            this.RequiresAuthentication ();

            Get["/cr/{status?}"] = parameters => {
                ChangeRequestModel model = new ChangeRequestModel();
                string status = (string)parameters.status;
                model.Planeswalker = (Planeswalker)this.Context.CurrentUser;
                model.Title = "M:tgDb.Info Admin";

                if(status == null)
                {
                    model.Changes = repository.GetChangeRequests().ToList();
                }
                else
                {
                    model.Changes = repository.GetChangeRequests(status).ToList();
                }

                return View["Change/ChangeRequests", model];
            };

            Get["/cards/{id}/logs"] = parameters => {
                CardLogsModel model =   new CardLogsModel();
                model.ActiveMenu =      "sets";
                model.Planeswalker =    (Planeswalker)this.Context.CurrentUser;
                model.Mvid =            (int)parameters.id;

                if(Request.Query.v != null)
                {
                    int version = 0; 
                    if(int.TryParse((string)Request.Query.v, out version))
                    {
                        model.NewVersion = version;
                    }
                }

                try
                {
                    model.Changes = repository.GetCardChangeRequests((int)parameters.id)
                    .OrderByDescending(x => x.Version) 
                    .ToList();
                }
                catch(Exception e)
                {
                    model.Errors.Add(e.Message);
                }

                return View["Change/CardLogs", model];
            };

            Get["/cards/{id}/logs/{logid}"] = parameters => {
                CardChange model = new CardChange();
                model.Planeswalker =    (Planeswalker)this.Context.CurrentUser;
            
                try
                {
                    model = repository.GetCardChangeRequest((Guid)parameters.logid);
                }
                catch(Exception e)
                {
                    model.Errors.Add(e.Message);
                }

                model.ActiveMenu =      "sets";
                model.Planeswalker =    (Planeswalker)this.Context.CurrentUser;
                model.Mvid =            (int)parameters.id;

               
                return View["Change/CardChange", model];
            };

            Post["/change/{id}/field/{field}"] = parameters => {
                Admin admin =               new Admin(ConfigurationManager.AppSettings.Get("api"));
                Planeswalker planeswalker = (Planeswalker)this.Context.CurrentUser;
                Guid changeId  =            Guid.Parse((string)parameters.id);
                string field =              (string)parameters.field;
                CardChange change =         null;

                if(!planeswalker.InRole("admin"))
                {
                    return HttpStatusCode.Unauthorized;
                }

                try
                {
                    change = repository.GetCardChangeRequest(changeId);

                    if(field == "close")
                    {
                        repository.UpdateCardChangeStatus(change.Id, "Closed");

                        return Response.AsRedirect(string.Format("/cards/{0}/logs/{1}",
                            change.Mvid, change.Id));
                    }

                    if(field == "open")
                    {
                        repository.UpdateCardChangeStatus(change.Id, "Pending");

                        return Response.AsRedirect(string.Format("/cards/{0}/logs/{1}",
                            change.Mvid, change.Id));
                    }


                    if(field == "formats")
                    {
                        admin.UpdateCardFormats(planeswalker.AuthToken,
                            change.Mvid, change.Formats);
                    }
                    else if(field == "rulings")
                    {
                        admin.UpdateCardRulings(planeswalker.AuthToken,
                            change.Mvid, change.Rulings);
                    }
                    else
                    {
                        string value = change.GetFieldValue(field);
                        admin.UpdateCardField(planeswalker.AuthToken,
                            change.Mvid, field, (string)value);
                    }
                        
                    repository.UpdateCardChangeStatus(change.Id, "Accepted", field);

                }
                catch(Exception e)
                {
                    throw e;
                }
                    
                return Response.AsRedirect(string.Format("/cards/{0}/logs/{1}",
                    change.Mvid, change.Id));
            };
                
            Get ["/cards/{id}/change"] = parameters => {
                CardChange model = new CardChange();
                Card card; 

                try
                {
                    card = magicdb.GetCard((int)parameters.id);
                    model = CardChange.MapCard(card);
                    model.Planeswalker = (Planeswalker)this.Context.CurrentUser;
                }
                catch(Exception e)
                {
                    throw e;
                }
                    
                return View["Change/Card", model];
            };

            Post ["/cards/{id}/change"] = parameters => {
                CardChange model =      this.Bind<CardChange>();
                Card current =          magicdb.GetCard((int)parameters.id);
                model.CardSetId =       current.CardSetId;
                model.CardSetName =     current.CardSetName;
                model.Name =            current.Name;
                model.Mvid =            (int)parameters.id;
                model.Rulings =         Bind.Rulings(Request);
                model.Formats =         Bind.Formats(Request);
                model.Planeswalker =    (Planeswalker)this.Context.CurrentUser;
                model.UserId =          model.Planeswalker.Id;
                CardChange card =       null;

                var result = this.Validate(model);

                if (!result.IsValid)
                {
                    model.Errors = ErrorUtility.GetValidationErrors(result);
                    return View["Change/Card", model];
                }
                    
                try
                {
                    card = repository.GetCardChangeRequest(
                        repository.AddCardChangeRequest(model)
                    );
                }
                catch(Exception e)
                {
                    model.Errors.Add(e.Message);
                    return View["Change/Card", model];
                }

                return Response.AsRedirect(string.Format("/cards/{0}/logs?v={1}", 
                    card.Mvid, card.Version));

                //return model.Description;
                //return View["Change/Card", model];

            };
                
            Post ["/cards/{id}/amount/{count}"] = parameters => {
                int multiverseId =  (int)parameters.id;
                int count =         (int)parameters.count;
                Guid walkerId =     ((Planeswalker)this.Context.CurrentUser).Id;
      
                repository.AddUserCard(walkerId,multiverseId,count);
               
                return count.ToString();
            };

            //TODO: Refactor this, to long and confusing 
            Get ["/mycards/{setId?}"] = parameters => {
                PlaneswalkerModel model =   new PlaneswalkerModel();
                model.ActiveMenu =          "mycards";
                string setId =              (string)parameters.setId;
                model.Planeswalker =        (Planeswalker)this.Context.CurrentUser;

                if(setId == null)
                {
                    //try to get an setId if no value was provided in the url
                    setId = repository.GetSetCardCounts(model.Planeswalker.Id)
                        .Select(x => x.Key)
                        .OrderBy(x => x).FirstOrDefault();

                    //if still no setId then user has no cards
                    if(setId == null)
                    {
                        PageModel pm = new PageModel();
                        pm.ActiveMenu =      "mycards";
                        pm.Planeswalker =    (Planeswalker)this.Context.CurrentUser;

                        pm.Errors.Add("You have no cards in this set or no cards in your collection");
                        return View["Page", pm];
                    }
                }
                    
                CardSet current = magicdb.GetSet(setId);
                //force type into block if block is null
                model.Block = current.Block ?? current.Type;
               
                try
                {
                    model.Counts = repository.GetSetCardCounts(model.Planeswalker.Id);
                    //if no counts then no cards again
                    if(model.Counts == null || 
                        model.Counts.Count == 0)
                    {
                        PageModel pm = new PageModel();
                        pm.ActiveMenu =      "mycards";
                        pm.Planeswalker =    (Planeswalker)this.Context.CurrentUser;

                        pm.Errors.Add("You have no cards in this set or no cards in your collection");
                        return View["Page", pm];
                    }

                    model.TotalCards = model.Counts.Sum(x => x.Value);
                    model.TotalAmount = repository.GetUserCards(model.Planeswalker.Id).Sum(x => x.Amount);
                    
                    List<string> blocks;

                    if(model.Counts.Count > 0)
                    {
                        model.Sets = magicdb.GetSets(model.Counts
                            .AsEnumerable().Select(x => x.Key)
                            .ToArray());

                        //keep a list of all sets user has cards in
                        //this is to get the default set for each block or type
                        model.AllSets = model.Sets;

                        //get all blocks
                        blocks = model.Sets
                            .Select(x => x.Block)
                            .Where(x => x != null)
                            .Distinct()
                            .OrderBy(x => x).ToList();
                        //force types into blocks
                        blocks.AddRange(model.Sets
                            .Where(x => x.Block == null)
                            .Select(x => x.Type)
                            .Distinct()
                            .OrderBy(x => x).ToList());

                        //macke dure no dupes
                        blocks = blocks.Distinct().ToList();

                        foreach(string block in blocks)
                        {
                            List<CardSet> sets = new List<CardSet>();
                            sets = model.Sets.Where(x => x.Block == block).ToList();
                            sets.AddRange(model.Sets.Where(x => x.Type == block && x.Block == null).ToList());
                        
                            int total = 0; 
                            foreach(CardSet set in sets)
                            {
                                total += model.Counts[set.Id];
                            }
                            if(block != null)
                            {
                                model.Blocks.Add(block,total);
                            }
                        }

                        //filter sets for current block or type
                        model.Sets = model.Sets
                            .Where(x => x.Block == model.Block || x.Type == model.Block)
                            .OrderBy(x => x.Name).ToArray();

                        //nothing in the set
                        if(model.Sets == null || model.Sets.Length == 0)
                        {
                            PageModel pm = new PageModel();
                            pm.ActiveMenu =      "mycards";
                            pm.Planeswalker =    (Planeswalker)this.Context.CurrentUser;

                            pm.Errors.Add("You have no cards in this set or no cards in your collection");
                            return View["Page", pm];
                 
                        }

                        if(setId == null)
                        {
                            setId = model.Sets.FirstOrDefault().Id;
                        }

                        model.SetId = setId;
                        model.UserCards = repository.GetUserCards(model.Planeswalker.Id, setId);

                        //man same here no cards
                        if(model.UserCards == null || 
                            model.UserCards.Length  == 0)
                        {
                            PageModel pm = new PageModel();
                            pm.ActiveMenu =      "mycards";
                            pm.Planeswalker =    (Planeswalker)this.Context.CurrentUser;

                            pm.Errors.Add("You have no cards in this set or no cards in your collection");
                            return View["Page", pm];
                        }

                        Card[] dbcards = null;
                        if(Request.Query.Show != null)
                        {
                            dbcards = magicdb.GetSetCards(model.SetId);
                            model.Show = true;
                        }
                        else
                        {
                            dbcards = magicdb.GetCards(model.UserCards
                                .AsEnumerable()
                                .Where(x => x.Amount > 0)
                                .Select(x => x.MultiverseId)
                                .ToArray());

                            model.Show = false;
                        }

                        //same here no cards, this is getting old 
                        if(dbcards == null || 
                            dbcards.Length == 0)
                        {
                            PageModel pm = new PageModel();
                            pm.ActiveMenu =      "mycards";
                            pm.Planeswalker =    (Planeswalker)this.Context.CurrentUser;

                            pm.Errors.Add("You have no cards in this set or no cards in your collection");
                            return View["Page", pm];
                        }

                        List<CardInfo> cards = new List<CardInfo>();

                        CardInfo card = null;
                        foreach(Card c in dbcards)
                        {
                            card = new CardInfo();
                            card.Amount = model.UserCards
                                .AsEnumerable()
                                .Where(x => x.MultiverseId == c.Id)
                                .Select(x => x.Amount)
                                .FirstOrDefault();

                            card.Card = c;
                        
                            cards.Add(card);
                        }

                       model.Cards = cards.OrderBy(x => x.Card.SetNumber).ToArray();
                    }
                    else
                    {
                        model.Messages.Add("You have no cards in your library.");
                    }
                }
                catch(Exception e)
                {
                    model.Errors.Add(e.Message);
                }

                return View["MyCards", model];
            };

            //can use this for public profile stuff
            Get ["/pw/{planeswalker}/cards"] = parameters => {
                PageModel model =       new PageModel();
                model.ActiveMenu =      "mycards";
                model.Planeswalker =    (Planeswalker)this.Context.CurrentUser;

                return View["Page", model];
            };
        }
    }
}

